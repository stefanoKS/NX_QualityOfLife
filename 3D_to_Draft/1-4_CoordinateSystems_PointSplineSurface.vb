Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms
Imports NXOpen
Imports NXOpen.Features
Imports NXOpenUI

' NX 2306 - associative datum coordinate systems based on:
'   origin: existing point from a feature group
'   Z: tangent of the chosen spline at nearest position to that point
'   Y: normal of the nearest face of the chosen surface feature
'   X: calculated by NX as a right-handed orthogonal axis
'
' The NXOpen Directions, Xform and DatumCsys are SMART objects with geometric
' parents; nothing is built from frozen XYZ/vector coordinates.
' NOTE: when surface normal is not perpendicular to spline tangent, NX
' orthogonalizes the Y axis. Z remains the primary tangent direction.
' Reverse Y: negate Y and X, keeping Z (original behavior).
' Reverse X while holding Y: reverse tangent Z; NX also negates X.
' Both: negate Y and Z, keeping X. All combinations remain right-handed.
'
Module CoordinateSystems_PointSplineSurface_NX2306
    Sub Main()
        Dim session As Session = Session.GetSession()
        Dim ui As NXOpen.UI = NXOpen.UI.GetUI()
        Dim part As Part = session.Parts.Work
        If part Is Nothing Then
            Msg(ui, "Open a work part first. / 作業パートを開いてください。", True)
            Return
        End If

        Dim groups As New List(Of PssChoice)()
        Dim splines As New List(Of PssChoice)()
        Dim surfaces As New List(Of PssChoice)()
        Dim features() As Feature = part.Features.GetFeatures()
        For Each feat As Feature In features
            If feat Is Nothing OrElse feat.Suppressed Then Continue For
            Try
                If TypeOf feat Is FeatureGroup Then
                    Dim candidateMembers() As Feature = Nothing
                    DirectCast(feat, FeatureGroup).GetMembers(candidateMembers)
                    If candidateMembers IsNot Nothing AndAlso candidateMembers.Length > 0 Then
                        Dim hasPoint As Boolean = False
                        For Each member As Feature In candidateMembers
                            If member Is Nothing OrElse member.Suppressed Then Continue For
                            For Each obj As NXObject In member.GetEntities()
                                If TypeOf obj Is NXOpen.Point Then
                                    hasPoint = True
                                    Exit For
                                End If
                            Next
                            If hasPoint Then Exit For
                        Next
                        If hasPoint Then groups.Add(New PssChoice(feat, Nothing))
                    End If
                Else
                    For Each obj As NXObject In feat.GetEntities()
                        If TypeOf obj Is NXOpen.Spline Then
                            splines.Add(New PssChoice(feat, obj))
                        End If
                    Next
                    Dim faceCount As Integer = 0
                    For Each body As NXOpen.Body In feat.GetBodies()
                        If body IsNot Nothing Then faceCount += body.GetFaces().Length
                    Next
                    If faceCount > 0 Then surfaces.Add(New PssChoice(feat, Nothing, faceCount))
                End If
            Catch
                ' Some feature types do not provide entities or bodies.
            End Try
        Next

        If groups.Count = 0 OrElse splines.Count = 0 OrElse surfaces.Count = 0 Then
            Msg(ui, "Missing geometry: need a point feature group, a spline, " &
                "and a surface/body feature in the work part." & Environment.NewLine & "作業パートに、点のフィーチャグループ、スプライン、サーフェス／ボディのフィーチャが必要です。", True)
            Return
        End If
        groups.Sort(Function(a, b) String.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase))
        splines.Sort(Function(a, b) a.Feature.Timestamp.CompareTo(b.Feature.Timestamp))
        surfaces.Sort(Function(a, b) String.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase))

        Dim groupChoice As PssChoice = Nothing
        Dim splineChoice As PssChoice = Nothing
        Dim surfaceChoice As PssChoice = Nothing
        Dim testOnly As Boolean = False
        Dim reverseY As Boolean = False
        Dim reverseXHoldingY As Boolean = False
        Using dialog As New PssPicker(groups, splines, surfaces)
            NXOpenUI.FormUtilities.SetApplicationIcon(dialog)
            NXOpenUI.FormUtilities.ReparentForm(dialog)
            If dialog.ShowDialog() <> DialogResult.OK Then Return
            groupChoice = dialog.GroupChoice
            splineChoice = dialog.SplineChoice
            surfaceChoice = dialog.SurfaceChoice
            testOnly = dialog.TestOnly
            reverseY = dialog.ReverseY
            reverseXHoldingY = dialog.ReverseXHoldingY
        End Using
        If groupChoice Is Nothing OrElse splineChoice Is Nothing OrElse surfaceChoice Is Nothing Then Return

        Dim group As FeatureGroup = DirectCast(groupChoice.Feature, FeatureGroup)
        Dim spline As NXOpen.Spline = DirectCast(splineChoice.Geometry, NXOpen.Spline)
        Dim members() As Feature = Nothing
        group.GetMembers(members)
        If members Is Nothing Then Return
        Dim ordered As New List(Of Feature)(members)
        ordered.RemoveAll(Function(f) f Is Nothing OrElse f.Suppressed)
        ordered.Sort(Function(a, b) a.Timestamp.CompareTo(b.Timestamp))
        Dim points As New List(Of NXOpen.Point)()
        For Each member As Feature In ordered
            For Each geom As NXObject In member.GetEntities()
                If TypeOf geom Is NXOpen.Point Then points.Add(DirectCast(geom, NXOpen.Point))
            Next
        Next
        If points.Count = 0 Then
            Msg(ui, "No points found in selected group. / 選択したグループに点がありません。", True)
            Return
        End If

        Dim mark As Session.UndoMarkId = session.SetUndoMark(
            Session.MarkVisibility.Visible, "Coordinate Systems from Points + Spline + Surface")
        Dim listing As ListingWindow = session.ListingWindow
        listing.Open()
        listing.WriteLine("=== ASSOC. CSYS FROM POINT / SPLINE / SURFACE ===")
        listing.WriteLine("Group=" & group.Name & "  spline=" & splineChoice.ToString())
        listing.WriteLine("Surface=" & surfaceChoice.ToString())
        listing.WriteLine("Axis signs relative to unflipped CSYS: " & AxisSigns(reverseY, reverseXHoldingY))
        Dim progress As String = "Preparation"
        Dim made As Integer = 0
        Try
            ' FACE FEATURE rule: includes all faces produced by selected surface
            ' feature. The direction selects the surface normal at the point's
            ' nearest location, and recomputes against that feature on update.
            progress = "Collecting faces of selected surface feature"
            Dim faceCollector As ScCollector = part.ScCollectors.CreateCollector()
            Dim faceRule As SelectionIntentRule = part.ScRuleFactory.CreateRuleFaceFeature(
                New Feature() {surfaceChoice.Feature})
            faceCollector.ReplaceRules(New SelectionIntentRule() {faceRule}, False)
            If faceCollector.GetObjects().Length = 0 Then
                Throw New InvalidOperationException("Selected feature's face rule returned zero faces.")
            End If

            Dim count As Integer = If(testOnly, 1, points.Count)
            For i As Integer = 0 To count - 1
                Dim sourcePoint As NXOpen.Point = points(i)
                progress = "Creating tangent at point " & (i + 1).ToString()
                Dim tangent As NXOpen.Direction = part.Directions.CreateDirectionOnPointParentCurve(
                    sourcePoint, CType(spline, NXOpen.IBaseCurve),
                    NXOpen.Direction.OnCurveOption.Tangent, If(reverseXHoldingY, Sense.Reverse, Sense.Forward),
                    SmartObject.UpdateOption.WithinModeling)

                progress = "Creating closest surface normal at point " & (i + 1).ToString()
                Dim normalSense As Sense = If(reverseY, Sense.Reverse, Sense.Forward)
                Dim faceNormal As NXOpen.Direction = part.Directions.CreateDirection(
                    faceCollector, sourcePoint, normalSense,
                    SmartObject.UpdateOption.WithinModeling)

                ' A stable Cartesian system needs two independent directions.
                ' Test the actual NX-computed directions, not fixed samples.
                progress = "Checking directions at point " & (i + 1).ToString()
                Dim zv As Vector3d = tangent.Vector
                Dim yv As Vector3d = faceNormal.Vector
                Dim dot As Double = zv.X * yv.X + zv.Y * yv.Y + zv.Z * yv.Z
                Dim zm As Double = Math.Sqrt(zv.X * zv.X + zv.Y * zv.Y + zv.Z * zv.Z)
                Dim ym As Double = Math.Sqrt(yv.X * yv.X + yv.Y * yv.Y + yv.Z * yv.Z)
                If zm < 0.000001 OrElse ym < 0.000001 OrElse
                    Math.Abs(dot / (zm * ym)) > 0.999 Then
                    Throw New InvalidOperationException(
                        "Tangent and face normal nearly parallel at point " & (i + 1).ToString() &
                        "; cannot construct stable orthogonal axes.")
                End If

                progress = "Creating associative coordinate system " & (i + 1).ToString()
                Dim smartXform As NXOpen.Xform = part.Xforms.CreateXformByPointYDirZDir(
                    sourcePoint, faceNormal, tangent,
                    SmartObject.UpdateOption.WithinModeling, 1.0)
                Dim csys As NXOpen.CartesianCoordinateSystem =
                    part.CoordinateSystems.CreateCoordinateSystem(
                        smartXform, SmartObject.UpdateOption.WithinModeling)

                Dim builder As DatumCsysBuilder = Nothing
                Try
                    builder = part.Features.CreateDatumCsysBuilder(CType(Nothing, Feature))
                    builder.Csys = csys
                    builder.DisplayScaleFactor = 1.0
                    Dim created As Feature = builder.CommitFeature()
                    If created Is Nothing Then Throw New InvalidOperationException("CommitFeature returned Nothing.")
                    created.SetName("SPLINE_CSYS_" & (i + 1).ToString("000"))
                Finally
                    If builder IsNot Nothing Then builder.Destroy()
                End Try
                made += 1
                listing.WriteLine((i + 1).ToString("000") & "  CSYS created; raw Y.Z=" &
                    (dot / (zm * ym)).ToString("F4", Globalization.CultureInfo.InvariantCulture))
            Next
            progress = "NX model update"
            Dim errors As Integer = session.UpdateManager.DoUpdate(mark)
            If errors <> 0 Then Throw New InvalidOperationException("Model update reported " & errors & " errors.")
            Msg(ui, "Created " & made.ToString() & " associative datum CSYS feature(s)." &
                Environment.NewLine & If(testOnly, "TEST MODE: first point only.", "All points processed.") &
                Environment.NewLine & "Origin=point; Z=signed spline tangent; Y=signed surface normal (orthogonalized)." &
                Environment.NewLine & "原点＝点、Z＝符号付き接線、Y＝符号付き面法線（直交化）。" &
                Environment.NewLine & AxisSigns(reverseY, reverseXHoldingY), False)
        Catch ex As Exception
            listing.WriteLine("FAILED during " & progress & ": " & ex.ToString())
            Try
                session.UndoToMark(mark, Nothing)
            Catch rollbackEx As Exception
                listing.WriteLine("ROLLBACK FAILED: " & rollbackEx.ToString())
            End Try
            Msg(ui, "Creation stopped during: " & progress & Environment.NewLine & ex.Message &
                Environment.NewLine & "Changes rolled back; see Listing Window.", True)
        End Try
    End Sub

    Friend Function AxisSigns(reverseY As Boolean, reverseXHoldingY As Boolean) As String
        Return "X=" & If(reverseY Xor reverseXHoldingY, "-", "+") &
            ", Y=" & If(reverseY, "-", "+") & ", Z=" & If(reverseXHoldingY, "-", "+")
    End Function

    Private Sub Msg(ui As NXOpen.UI, text As String, err As Boolean)
        ui.NXMessageBox.Show("Point / Spline / Surface CSYS",
            If(err, NXMessageBox.DialogType.Error, NXMessageBox.DialogType.Information), text)
    End Sub

    Public Function GetUnloadOption(ByVal dummy As String) As Integer
        Return CInt(Session.LibraryUnloadOption.Immediately)
    End Function
End Module

Friend NotInheritable Class PssChoice
    Public ReadOnly Feature As NXOpen.Features.Feature
    Public ReadOnly Geometry As NXOpen.NXObject
    Private ReadOnly FaceCount As Integer
    Public Sub New(f As NXOpen.Features.Feature, o As NXOpen.NXObject, Optional n As Integer = 0)
        Feature = f
        Geometry = o
        FaceCount = n
    End Sub
    Public Overrides Function ToString() As String
        Dim nm As String = Feature.Name
        If String.IsNullOrWhiteSpace(nm) Then nm = Feature.FeatureType
        Dim suffix As String = If(FaceCount > 0, "  [faces: " & FaceCount.ToString() & "]", "")
        Return nm & "  [#" & Feature.Timestamp.ToString() & "]" & suffix
    End Function
End Class

Friend NotInheritable Class PssPicker
    Inherits System.Windows.Forms.Form
    Private ReadOnly groupBox As ComboBox
    Private ReadOnly splineBox As ComboBox
    Private ReadOnly surfaceBox As ComboBox
    Private ReadOnly testBox As CheckBox
    Private ReadOnly flipBox As CheckBox
    Private ReadOnly flipXBox As CheckBox
    Private ReadOnly axisInfo As System.Windows.Forms.Label

    Public ReadOnly Property GroupChoice As PssChoice
        Get
            Return TryCast(groupBox.SelectedItem, PssChoice)
        End Get
    End Property
    Public ReadOnly Property SplineChoice As PssChoice
        Get
            Return TryCast(splineBox.SelectedItem, PssChoice)
        End Get
    End Property
    Public ReadOnly Property SurfaceChoice As PssChoice
        Get
            Return TryCast(surfaceBox.SelectedItem, PssChoice)
        End Get
    End Property
    Public ReadOnly Property TestOnly As Boolean
        Get
            Return testBox.Checked
        End Get
    End Property
    Public ReadOnly Property ReverseY As Boolean
        Get
            Return flipBox.Checked
        End Get
    End Property
    Public ReadOnly Property ReverseXHoldingY As Boolean
        Get
            Return flipXBox.Checked
        End Get
    End Property

    Public Sub New(groups As IList(Of PssChoice), splines As IList(Of PssChoice), surfaces As IList(Of PssChoice))
        MyBase.New()
        Text = "Associative Datum CSYS / 関連付けデータム座標系"
        Font = New Font("Yu Gothic UI", 10.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(820, 710)
        MinimumSize = New Size(820, 710)
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        ShowInTaskbar = False
        MinimizeBox = False
        MaximizeBox = False
        Dim panel As New TableLayoutPanel()
        panel.Dock = DockStyle.Fill
        panel.Padding = New Padding(15)
        panel.ColumnCount = 1
        panel.RowCount = 11
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        For i As Integer = 0 To 5
            panel.RowStyles.Add(New RowStyle(SizeType.Absolute, If(i Mod 2 = 0, 52.0F, 38.0F)))
        Next
        panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 56.0F))
        panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 56.0F))
        panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 56.0F))
        panel.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 43.0F))
        groupBox = AddCombo(panel, "1. Point feature group (origins)" & Environment.NewLine & "1. 原点となる点のフィーチャグループ", 0, groups)
        splineBox = AddCombo(panel, "2. Spline (nearest tangent defines Z)" & Environment.NewLine & "2. スプライン（点に最も近い位置の接線をZ軸に使用）", 2, splines)
        surfaceBox = AddCombo(panel, "3. Surface/body feature (nearest face normal defines Y)" & Environment.NewLine &
            "3. サーフェス／ボディのフィーチャ（最も近い面の法線からY軸を設定）", 4, surfaces)
        If splineBox.Items.Count > 0 Then splineBox.SelectedIndex = splineBox.Items.Count - 1
        For i As Integer = 0 To surfaceBox.Items.Count - 1
            If surfaceBox.Items(i).ToString().IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) >= 0 Then
                surfaceBox.SelectedIndex = i
                Exit For
            End If
        Next
        testBox = New CheckBox With {.Text = "Test first point only (off by default: process all points)." & Environment.NewLine &
            "最初の点のみテスト（既定はオフ：全点を処理します）。", .Checked = False,
                                    .Dock = DockStyle.Fill}
        panel.Controls.Add(testBox, 0, 6)
        flipBox = New CheckBox With {.Text = "Reverse Y and X; keep Z (original flip)" & Environment.NewLine &
            "Y軸（面法線）とX軸を反転、Z軸は維持（従来の反転）", .Dock = DockStyle.Fill}
        panel.Controls.Add(flipBox, 0, 7)
        flipXBox = New CheckBox With {.Text = "Reverse X and Z; keep Y" & Environment.NewLine &
            "X軸とZ軸（接線方向）を反転、Y軸は維持", .Dock = DockStyle.Fill}
        panel.Controls.Add(flipXBox, 0, 8)
        axisInfo = New System.Windows.Forms.Label With {
            .ForeColor = SystemColors.GrayText, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft}
        panel.Controls.Add(axisInfo, 0, 9)
        AddHandler flipBox.CheckedChanged, AddressOf UpdateAxisInfo
        AddHandler flipXBox.CheckedChanged, AddressOf UpdateAxisInfo
        UpdateAxisInfo(Me, EventArgs.Empty)
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.RightToLeft, .WrapContents = False}
        Dim cancelBtn As New Button With {.Text = "Cancel / キャンセル", .Size = New Size(155, 34),
            .DialogResult = DialogResult.Cancel}
        Dim createBtn As New Button With {.Text = "Create CSYS / 座標系を作成", .Size = New Size(220, 34),
            .DialogResult = DialogResult.OK}
        actions.Controls.Add(cancelBtn)
        actions.Controls.Add(createBtn)
        panel.Controls.Add(actions, 0, 10)
        Controls.Add(panel)
        AcceptButton = createBtn
        CancelButton = cancelBtn
    End Sub

    Private Sub UpdateAxisInfo(sender As Object, e As EventArgs)
        axisInfo.Text = "Relative to the unflipped CSYS / 反転なしに対する軸の符号: " &
            CoordinateSystems_PointSplineSurface_NX2306.AxisSigns(ReverseY, ReverseXHoldingY) & Environment.NewLine &
            "Both checked: Y and Z reverse; X stays unchanged. Z = X cross Y (right-handed)." & Environment.NewLine &
            "両方オン：Y軸・Z軸を反転し、X軸を維持します。常に右手系です。" & Environment.NewLine &
            "Y is the surface normal orthogonalized against Z. / Y軸は面法線をZ軸に対して直交化します。"
    End Sub

    Private Function AddCombo(panel As TableLayoutPanel, titleText As String, row As Integer,
                              items As IList(Of PssChoice)) As ComboBox
        Dim lbl As New System.Windows.Forms.Label With {.Text = titleText, .Dock = DockStyle.Fill,
                                                        .TextAlign = ContentAlignment.MiddleLeft}
        panel.Controls.Add(lbl, 0, row)
        Dim combo As New ComboBox With {.Dock = DockStyle.Top,
            .DropDownStyle = ComboBoxStyle.DropDownList, .MaxDropDownItems = 12,
            .IntegralHeight = False}
        For Each item As PssChoice In items
            combo.Items.Add(item)
        Next
        If combo.Items.Count > 0 Then combo.SelectedIndex = 0
        panel.Controls.Add(combo, 0, row + 1)
        Return combo
    End Function
End Class
