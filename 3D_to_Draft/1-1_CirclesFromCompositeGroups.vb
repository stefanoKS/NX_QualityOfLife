Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.UF
Imports NXOpenUI

' NX 2306 - Create one ASSOCIATIVE full circle per Composite Curve feature.
' Input: named Feature Group (dropdown), containing Composite Curve features.
' The longest connected, nonbranching output-curve chain of each composite is
' sampled at 1/3, 2/3, 1 of its TOTAL arc length (not parameter).
' Curve samples are NX smart points, referenced by the associative circle.
' No intermediate point features are added to the Part Navigator.
' Run from Tools > Journal > Play, or a Journal File ribbon button.
Module CirclesFromCompositeGroups

    ' Distance tolerance for matching adjoining curve ends, in part units.
    ' Change only if composite output curves have noticeably larger gaps.
    Private Const CONNECT_MM As Double = 0.01
    Private Const CONNECT_INCH As Double = 0.0004

    Private Class CurveSegment
        Public Geometry As IBaseCurve
        Public NxTag As Tag
        Public First As Point3d
        Public Last As Point3d
        Public Length As Double
        Public NodeA As Integer
        Public NodeB As Integer
    End Class

    Private Class OrientedSegment
        Public Segment As CurveSegment
        Public Reversed As Boolean
    End Class

    Private Class Chain
        Public Elements As New List(Of OrientedSegment)()
        Public Length As Double
        Public TotalSegments As Integer
    End Class

    Sub Main()
        Dim session As Session = Session.GetSession()
        Dim ui As NXOpen.UI = NXOpen.UI.GetUI()
        Dim part As Part = session.Parts.Work
        If part Is Nothing Then
            Alert(ui, "Open a work part first. / 作業パートを開いてください。", True)
            Return
        End If

        Dim groups As New List(Of FeatureGroup)()
        Dim names As New List(Of String)()
        For Each feat As Feature In part.Features.GetFeatures()
            If TypeOf feat Is FeatureGroup Then
                Dim grp As FeatureGroup = DirectCast(feat, FeatureGroup)
                If Not String.IsNullOrWhiteSpace(grp.Name) Then
                    groups.Add(grp)
                    names.Add(grp.Name)
                End If
            End If
        Next
        If names.Count = 0 Then
            Alert(ui, "No named feature groups in the work part. / 作業パートに名前付きフィーチャグループがありません。", True)
            Return
        End If
        names.Sort(StringComparer.OrdinalIgnoreCase)

        Dim inputName As String = Nothing
        Using picker As New CompositeGroupPicker(names, names(0))
            NXOpenUI.FormUtilities.SetApplicationIcon(picker)
            NXOpenUI.FormUtilities.ReparentForm(picker)
            If picker.ShowDialog() <> DialogResult.OK Then Return
            inputName = picker.SelectedGroupName
        End Using
        If String.IsNullOrWhiteSpace(inputName) Then Return

        Dim group As FeatureGroup = Nothing
        Dim matches As Integer = 0
        For Each g As FeatureGroup In groups
            If String.Equals(g.Name, inputName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                group = g
                matches += 1
            End If
        Next
        If matches <> 1 Then
            Alert(ui, "Expected one group named '" & inputName.Trim() & "'. Found: " & matches.ToString() &
                Environment.NewLine & "指定名のグループが1つに特定できません。名前を確認してください。", True)
            Return
        End If

        Dim memberArray() As Feature = Nothing
        group.GetMembers(memberArray)
        If memberArray Is Nothing OrElse memberArray.Length = 0 Then
            Alert(ui, "Group '" & group.Name & "' is empty. / 選択したグループは空です。", True)
            Return
        End If

        Dim composites As New List(Of Feature)()
        For Each feature As Feature In memberArray
            If Not feature.Suppressed AndAlso TypeOf feature Is CompositeCurve Then
                composites.Add(feature)
            End If
        Next
        composites.Sort(Function(a As Feature, b As Feature) a.Timestamp.CompareTo(b.Timestamp))
        If composites.Count = 0 Then
            Alert(ui, "No unsuppressed Composite Curve features found in '" & group.Name & "'." &
                Environment.NewLine & "選択したグループに抑制されていない複合曲線がありません。", True)
            Return
        End If

        Dim uf As UFSession = UFSession.GetUFSession()
        Dim joinTol As Double = If(part.PartUnits = BasePart.Units.Inches, CONNECT_INCH, CONNECT_MM)
        Dim mainMark As Session.UndoMarkId = session.SetUndoMark( _
            Session.MarkVisibility.Visible, "Circles From Composite Curves")

        Dim count As Integer = 0
        Dim discardedCount As Integer = 0
        Dim failures As New List(Of String)()
        Dim report As ListingWindow = session.ListingWindow
        report.Open()
        report.WriteLine("Composite -> associative full circle | group: " & group.Name)
        report.WriteLine("Samples: 33.333%, 66.667%, 100% of longest connected chain")

        For Each composite As Feature In composites
            Dim subMark As Session.UndoMarkId = session.SetUndoMark( _
                Session.MarkVisibility.Invisible, "Circle from " & composite.GetFeatureName())
            Try
                Dim rawEntities() As NXObject = composite.GetEntities()
                Dim segments As New List(Of CurveSegment)()
                For Each entity As NXObject In rawEntities
                    If TypeOf entity Is Curve OrElse TypeOf entity Is Edge Then
                        Dim seg As CurveSegment = DescribeCurve(entity, uf)
                        If seg.Length > 1.0E-9 Then segments.Add(seg)
                    End If
                Next
                If segments.Count = 0 Then Throw New InvalidOperationException( _
                    "No valid output curves in this composite feature.")

                Dim chosen As Chain = LongestNonBranchingChain(segments, joinTol)
                If chosen.Elements.Count = 0 OrElse chosen.Length <= 0.0 Then
                    Throw New InvalidOperationException("Unable to obtain a continuous curve chain.")
                End If

                Dim samplePoints(2) As NXOpen.Point
                Dim fractions() As Double = {1.0 / 3.0, 2.0 / 3.0, 1.0}
                For i As Integer = 0 To 2
                    samplePoints(i) = CreateArcLengthSample(part, chosen, fractions(i))
                Next

                ' A three-point circle is ill-conditioned for near-collinear or
                ' duplicate positions; fail before creating a bad hole circle.
                ValidateThreePoints(samplePoints(0).Coordinates, _
                                    samplePoints(1).Coordinates, _
                                    samplePoints(2).Coordinates, joinTol)

                Dim builder As AssociativeArcBuilder = Nothing
                Try
                    builder = part.BaseFeatures.CreateAssociativeArcBuilder(CType(Nothing, NXOpen.Arc))
                    builder.Associative = True
                    builder.Type = AssociativeArcBuilder.Types.ThreePointArc
                    builder.StartPointOptions = AssociativeArcBuilder.StartOption.Point
                    builder.MidPointOptions = AssociativeArcBuilder.MidOption.Point
                    builder.EndPointOptions = AssociativeArcBuilder.EndOption.Point
                    builder.StartPoint.Value = samplePoints(0)
                    builder.MidPoint.Value = samplePoints(1)
                    builder.EndPoint.Value = samplePoints(2)
                    builder.Limits.FullCircle = True

                    Dim circleFeature As Feature = builder.CommitFeature()
                    If circleFeature Is Nothing Then
                        Throw New InvalidOperationException("NX returned no circle feature.")
                    End If
                    circleFeature.SetName("HOLE_CIRCLE_" & (count + 1).ToString("000"))
                    ' Guard against a partial 3-point arc if FullCircle is ignored.
                    Dim circleFound As Boolean = False
                    For Each result As NXObject In circleFeature.GetEntities()
                        If TypeOf result Is NXOpen.Arc Then
                            Dim closedFlag As Integer = uf.Modl.AskCurveClosed(result.Tag)
                            If closedFlag = 0 Then
                                Throw New InvalidOperationException("Result is an open arc, not a full circle.")
                            End If
                            circleFound = True
                        End If
                    Next
                    If Not circleFound Then
                        Throw New InvalidOperationException("Result has no arc/circle geometry.")
                    End If
                Finally
                    If builder IsNot Nothing Then builder.Destroy()
                End Try

                Dim updateErrors As Integer = session.UpdateManager.DoUpdate(subMark)
                If updateErrors <> 0 Then
                    Throw New InvalidOperationException("NX reported " & updateErrors.ToString() & " update error(s).")
                End If

                Dim skipped As Integer = segments.Count - chosen.TotalSegments
                discardedCount += skipped
                count += 1
                report.WriteLine("OK " & composite.GetFeatureName() & " -> HOLE_CIRCLE_" & count.ToString("000") & _
                    ", total chain length=" & chosen.Length.ToString("F4") & _
                    ", segments=" & chosen.TotalSegments.ToString() & _
                    ", excluded disconnected segments=" & skipped.ToString())
                session.DeleteUndoMark(subMark, Nothing)
            Catch ex As Exception
                Try
                    session.UndoToMark(subMark, "Circle from composite failed")
                Catch
                    report.WriteLine("WARNING: rollback failed for " & composite.GetFeatureName() & ". Check NX Undo history!")
                End Try
                failures.Add(composite.GetFeatureName() & ": " & ex.Message)
                report.WriteLine("SKIP " & composite.GetFeatureName() & ": " & ex.Message)
            End Try
        Next

        report.WriteLine("Summary: " & count.ToString() & " created, " & failures.Count.ToString() & _
                         " skipped; " & discardedCount.ToString() & " nonchosen curve segment(s).")
        If count = 0 Then
            Try
                session.DeleteUndoMark(mainMark, Nothing)
            Catch
            End Try
        End If

        Dim summary As String = count.ToString() & " associative full circle(s) created from " & _
            composites.Count.ToString() & " composite feature(s)." & Environment.NewLine & _
            "Source group: " & group.Name & Environment.NewLine & _
            "Nonselected disconnected segments: " & discardedCount.ToString() & _
            Environment.NewLine & "Skipped composites: " & failures.Count.ToString() & _
            Environment.NewLine & "Details: NX Listing Window."
        Alert(ui, summary, count = 0)
    End Sub

    Private Function DescribeCurve(entity As NXObject, uf As UFSession) As CurveSegment
        Dim geo As IBaseCurve = DirectCast(entity, IBaseCurve)
        Dim s As New CurveSegment()
        s.Geometry = geo
        s.NxTag = entity.Tag
        Dim st(2) As Double
        Dim en(2) As Double
        Dim tangent(2) As Double
        Dim principal(2) As Double
        Dim binormal(2) As Double
        Dim torsion As Double = 0.0
        Dim radius As Double = 0.0
        ' Curve parameters are not necessarily 0.0 and 1.0 (e.g. arcs/splines).
        ' Evaluate the actual start/end of the curve parameter interval.
        Dim domain(1) As Double
        Dim periodic As Integer = 0
        uf.Curve.AskParameterization(s.NxTag, domain, periodic)
        uf.Modl.AskCurveProps(s.NxTag, domain(0), st, tangent, principal, binormal, torsion, radius)
        uf.Modl.AskCurveProps(s.NxTag, domain(1), en, tangent, principal, binormal, torsion, radius)
        s.First = New Point3d(st(0), st(1), st(2))
        s.Last = New Point3d(en(0), en(1), en(2))
        Dim value As Double = 0.0
        uf.Curve.AskArcLength(s.NxTag, domain(0), domain(1), ModlUnits.ModlUnitsPart, value)
        s.Length = Math.Abs(value)
        Return s
    End Function

    Private Function NearestOrNewNode(nodes As List(Of Point3d), p As Point3d, tol As Double) As Integer
        For i As Integer = 0 To nodes.Count - 1
            If Distance(nodes(i), p) <= tol Then Return i
        Next
        nodes.Add(p)
        Return nodes.Count - 1
    End Function

    Private Function LongestNonBranchingChain(segments As List(Of CurveSegment), tol As Double) As Chain
        Dim nodes As New List(Of Point3d)()
        Dim incident As New Dictionary(Of Integer, List(Of Integer))()
        For i As Integer = 0 To segments.Count - 1
            Dim s As CurveSegment = segments(i)
            s.NodeA = NearestOrNewNode(nodes, s.First, tol)
            s.NodeB = NearestOrNewNode(nodes, s.Last, tol)
            If Not incident.ContainsKey(s.NodeA) Then incident(s.NodeA) = New List(Of Integer)()
            If Not incident.ContainsKey(s.NodeB) Then incident(s.NodeB) = New List(Of Integer)()
            incident(s.NodeA).Add(i)
            incident(s.NodeB).Add(i) ' self-loop deliberately counts twice
        Next

        Dim seen As New List(Of Integer)()
        Dim bestSegments As New List(Of Integer)()
        Dim bestLength As Double = -1.0
        For i As Integer = 0 To segments.Count - 1
            If seen.Contains(i) Then Continue For
            Dim component As New List(Of Integer)()
            Dim queue As New Queue(Of Integer)()
            queue.Enqueue(i)
            seen.Add(i)
            Dim length As Double = 0.0
            While queue.Count > 0
                Dim curr As Integer = queue.Dequeue()
                component.Add(curr)
                length += segments(curr).Length
                For Each node As Integer In New Integer() {segments(curr).NodeA, segments(curr).NodeB}
                    For Each adjacent As Integer In incident(node)
                        If Not seen.Contains(adjacent) Then
                            seen.Add(adjacent)
                            queue.Enqueue(adjacent)
                        End If
                    Next
                Next
            End While
            If length > bestLength Then
                bestLength = length
                bestSegments = component
            End If
        Next
        ' The longest COMPONENT must itself be an unbranched chain or loop.
        ' Do not silently use a shorter component when the longest branches.
        Dim degree As New Dictionary(Of Integer, Integer)()
        For Each index As Integer In bestSegments
            Dim s As CurveSegment = segments(index)
            If Not degree.ContainsKey(s.NodeA) Then degree(s.NodeA) = 0
            If Not degree.ContainsKey(s.NodeB) Then degree(s.NodeB) = 0
            degree(s.NodeA) += 1
            degree(s.NodeB) += 1
        Next
        Dim ends As New List(Of Integer)()
        For Each kv As KeyValuePair(Of Integer, Integer) In degree
            If kv.Value > 2 Then Throw New InvalidOperationException( _
                "Longest connected component branches at a junction; cannot determine one traversal.")
            If kv.Value = 1 Then ends.Add(kv.Key)
        Next
        If ends.Count <> 0 AndAlso ends.Count <> 2 Then
            Throw New InvalidOperationException("Invalid curve topology (expected 0 or 2 chain endpoints).")
        End If

        Dim startNode As Integer
        If ends.Count = 2 Then
            ' Deterministic: begin at the first endpoint encountered in the output list.
            Dim first As CurveSegment = segments(bestSegments(0))
            If ends.Contains(first.NodeA) Then
                startNode = first.NodeA
            ElseIf ends.Contains(first.NodeB) Then
                startNode = first.NodeB
            Else
                startNode = Math.Min(ends(0), ends(1))
            End If
        Else
            ' Closed loop: starting seam is the first returned segment's first end.
            startNode = segments(bestSegments(0)).NodeA
        End If

        Dim remaining As New List(Of Integer)(bestSegments)
        Dim selected As New Chain()
        selected.TotalSegments = bestSegments.Count
        selected.Length = bestLength
        Dim nodeCursor As Integer = startNode
        While remaining.Count > 0
            Dim nextIndex As Integer = -1
            For Each candidate As Integer In incident(nodeCursor)
                If remaining.Contains(candidate) Then
                    nextIndex = candidate
                    Exit For
                End If
            Next
            If nextIndex = -1 Then Throw New InvalidOperationException("Could not order connected curve segments.")
            Dim s As CurveSegment = segments(nextIndex)
            Dim entry As New OrientedSegment()
            entry.Segment = s
            entry.Reversed = (s.NodeB = nodeCursor AndAlso s.NodeA <> nodeCursor)
            selected.Elements.Add(entry)
            remaining.Remove(nextIndex)
            nodeCursor = If(entry.Reversed, s.NodeA, s.NodeB)
        End While
        Return selected
    End Function

    Private Function CreateArcLengthSample(part As Part, chain As Chain, fraction As Double) As NXOpen.Point
        Dim target As Double = chain.Length * fraction
        Dim used As Double = 0.0
        For i As Integer = 0 To chain.Elements.Count - 1
            Dim item As OrientedSegment = chain.Elements(i)
            Dim segLength As Double = item.Segment.Length
            If target <= used + segLength OrElse i = chain.Elements.Count - 1 Then
                Dim localDist As Double = Math.Max(0.0, Math.Min(segLength, target - used))
                Dim pct As Double = 100.0 * localDist / segLength
                If item.Reversed Then pct = 100.0 - pct
                pct = Math.Max(0.0, Math.Min(100.0, pct))
                Dim location As Scalar = part.Scalars.CreateScalar( _
                    pct, Scalar.DimensionalityType.None, SmartObject.UpdateOption.WithinModeling)
                Dim point As NXOpen.Point = part.Points.CreatePoint( _
                    item.Segment.Geometry, location, _
                    PointCollection.PointOnCurveLocationOption.PercentArcLength, _
                    SmartObject.UpdateOption.WithinModeling)
                point.SetVisibility(SmartObject.VisibilityOption.Invisible)
                Return point
            End If
            used += segLength
        Next
        Throw New InvalidOperationException("Arc-length sampling exceeded the curve chain.")
    End Function

    Private Sub ValidateThreePoints(a As Point3d, b As Point3d, c As Point3d, tol As Double)
        Dim ab As Double = Distance(a, b)
        Dim bc As Double = Distance(b, c)
        Dim ac As Double = Distance(a, c)
        If ab < tol OrElse bc < tol OrElse ac < tol Then
            Throw New InvalidOperationException("Sample locations overlap (cannot define a circle).")
        End If
        Dim x1 As Double = b.X - a.X, y1 As Double = b.Y - a.Y, z1 As Double = b.Z - a.Z
        Dim x2 As Double = c.X - a.X, y2 As Double = c.Y - a.Y, z2 As Double = c.Z - a.Z
        Dim cx As Double = y1 * z2 - z1 * y2
        Dim cy As Double = z1 * x2 - x1 * z2
        Dim cz As Double = x1 * y2 - y1 * x2
        Dim normalizedArea As Double = Math.Sqrt(cx * cx + cy * cy + cz * cz) / (ab * ac)
        If normalizedArea < 0.0001 Then
            Throw New InvalidOperationException("Three sampled points are nearly collinear.")
        End If
    End Sub

    Private Function Distance(a As Point3d, b As Point3d) As Double
        Dim x As Double = a.X - b.X, y As Double = a.Y - b.Y, z As Double = a.Z - b.Z
        Return Math.Sqrt(x * x + y * y + z * z)
    End Function

    Private Sub Alert(ui As NXOpen.UI, msg As String, errorState As Boolean)
        Dim kind As NXMessageBox.DialogType = NXMessageBox.DialogType.Information
        If errorState Then kind = NXMessageBox.DialogType.Error
        ui.NXMessageBox.Show("Circle From Composite Curves", kind, msg)
    End Sub

    Public Function GetUnloadOption(ByVal dummy As String) As Integer
        Return CInt(Session.LibraryUnloadOption.Immediately)
    End Function
End Module

Friend NotInheritable Class CompositeGroupPicker
    Inherits System.Windows.Forms.Form
    Private ReadOnly selector As ComboBox
    Public ReadOnly Property SelectedGroupName As String
        Get
            Return selector.Text.Trim()
        End Get
    End Property

    Public Sub New(names As IEnumerable(Of String), defaultName As String)
        MyBase.New()
        Me.Text = "Circle From Composite Curves / 複合曲線から円を作成"
        Me.Font = New Font("Yu Gothic UI", 10.0F, FontStyle.Regular)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(720, 260)
        Me.MinimumSize = New Size(720, 260)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False

        Dim layout As New TableLayoutPanel()
        layout.Dock = DockStyle.Fill
        layout.Padding = New Padding(16, 12, 16, 12)
        layout.ColumnCount = 1
        layout.RowCount = 4
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 60.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 44.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))

        Dim header As New Label()
        header.Text = "Select a feature group containing Composite Curves:" & Environment.NewLine &
            "複合曲線を含むフィーチャグループを選択してください："
        header.Dock = DockStyle.Fill
        header.TextAlign = ContentAlignment.MiddleLeft
        layout.Controls.Add(header, 0, 0)

        selector = New ComboBox()
        selector.Dock = DockStyle.Top
        selector.DropDownStyle = ComboBoxStyle.DropDown
        selector.AutoCompleteMode = AutoCompleteMode.SuggestAppend
        selector.AutoCompleteSource = AutoCompleteSource.ListItems
        selector.IntegralHeight = False
        selector.MaxDropDownItems = 12
        For Each name As String In names
            selector.Items.Add(name)
        Next
        selector.SelectedItem = defaultName
        layout.Controls.Add(selector, 0, 1)

        Dim hint As New Label()
        hint.Text = "One full circle per composite; uses its longest connected chain." & Environment.NewLine &
            "複合曲線ごとに円を1つ作成します。分離した曲線がある場合は最長の連続部分を使用します。"
        hint.Dock = DockStyle.Fill
        hint.TextAlign = ContentAlignment.MiddleLeft
        hint.ForeColor = SystemColors.GrayText
        layout.Controls.Add(hint, 0, 2)

        Dim buttons As New FlowLayoutPanel()
        buttons.Dock = DockStyle.Fill
        buttons.FlowDirection = FlowDirection.RightToLeft
        buttons.WrapContents = False
        buttons.Margin = New Padding(0)
        Dim cancelBtn As New Button()
        cancelBtn.Text = "Cancel / キャンセル"
        cancelBtn.DialogResult = DialogResult.Cancel
        cancelBtn.Size = New Size(155, 34)
        buttons.Controls.Add(cancelBtn)
        Dim okBtn As New Button()
        okBtn.Text = "OK / 実行"
        okBtn.DialogResult = DialogResult.OK
        okBtn.Size = New Size(130, 34)
        buttons.Controls.Add(okBtn)
        layout.Controls.Add(buttons, 0, 3)
        Me.AcceptButton = okBtn
        Me.CancelButton = cancelBtn
        Me.Controls.Add(layout)
    End Sub
End Class
