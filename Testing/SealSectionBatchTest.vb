Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.UF

' Initial NX 2406 batch test. One sphere per CSYS, one Boolean per sheet.
' Retains original target/tool using settings from intersectSheet.vb.
' If whole-sheet Intersect fails, retry associative single-face copies at the same tolerance.
' A conflicting single-face Boolean result is retried with Split Body on the copy only.
' Confirmed empty intersections are skipped; other unresolved faces roll back the batch.
' Grid is row-major, top-left first, at 1:1 scale. CSYS origins align to cell centers.
Module SealSectionBatchTest
    Private Const Title As String = "Batch Seal Sections"
    Private Const Tolerance As Double = 0.01
    ' NX 2406: "Tool body completely outside target body." Do not match message text.
    Private Const NoIntersectionError As Integer = 670030
    Private Const IntersectionGapError As Integer = 670362

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim part As Part = session.Parts.Work
        Dim mark As Session.UndoMarkId
        Dim changed As Boolean = False
        Dim stage As String = "checking inputs"
        Try
            If part Is Nothing OrElse part.PartUnits <> BasePart.Units.Millimeters Then
                Throw New InvalidOperationException("Open a millimeter part in Modeling.")
            End If
            If session.Parts.Display Is Nothing OrElse session.Parts.Display.Tag <> part.Tag Then Throw New InvalidOperationException("Display the work part before running this journal.")
            Dim surfaces As FeatureGroup = ChooseGroup(part, "Step 1 - Surface feature group")
            If surfaces Is Nothing Then Return
            Dim sectors As FeatureGroup = ChooseGroup(part, "Step 2 - CSYS feature group")
            If sectors Is Nothing Then Return
            Dim bodyList As New List(Of Body)()
            Dim bodyTags As New List(Of Tag)()
            For Each feature As Feature In Members(surfaces)
                For Each body As Body In feature.GetBodies()
                    If body.IsSheetBody AndAlso Not body.IsOccurrence AndAlso Not bodyTags.Contains(body.Tag) Then
                        bodyTags.Add(body.Tag)
                        bodyList.Add(body)
                    End If
                Next
            Next
            Dim datums As New List(Of DatumCsys)()
            For Each feature As Feature In Members(sectors)
                If TypeOf feature Is DatumCsys Then datums.Add(DirectCast(feature, DatumCsys))
            Next
            datums.Sort(Function(left, right) left.Timestamp.CompareTo(right.Timestamp))
            If bodyList.Count = 0 OrElse datums.Count = 0 Then Throw New InvalidOperationException("The groups must contain sheet bodies and Datum CSYS features respectively.")
            Dim radius As Double
            Dim width As Double
            Dim height As Double
            Dim gap As Double
            If Not ReadNumber("Sphere radius in mm", "", False, radius) Then Return
            If Not ReadNumber("Drawing width in mm", "5000", False, width) Then Return
            If Not ReadNumber("Drawing height in mm", "5000", False, height) Then Return
            If Not ReadNumber("Gap between sectors in mm (also used at the sheet edges)", "10", True, gap) Then Return
            Dim pitch As Double = 2 * radius + gap
            If Double.IsInfinity(pitch) Then Throw New InvalidOperationException("Radius is too large.")
            Dim columnsFit As Double = Math.Floor((width - gap) / pitch)
            Dim rowsFit As Double = Math.Floor((height - gap) / pitch)
            If columnsFit < 1 OrElse rowsFit < 1 OrElse columnsFit * rowsFit < datums.Count Then
                Throw New InvalidOperationException("Drawing is too small for " & datums.Count.ToString() &
                    " sectors at 1:1. Capacity is " & Math.Max(0, columnsFit).ToString() & " columns x " &
                    Math.Max(0, rowsFit).ToString() & " rows. Each cell needs a sphere diameter of " &
                    (2 * radius).ToString() & " mm plus the entered gap. Increase the drawing dimensions or reduce the radius/gap.")
            End If
            Dim columns As Integer = CInt(Math.Min(columnsFit, datums.Count))
            Dim rows As Integer = CInt(Math.Ceiling(CDbl(datums.Count) / columns))
            session.ListingWindow.Open()
            session.ListingWindow.WriteLine("Grid " & columns.ToString() & " x " & rows.ToString() & "; ascending CSYS timestamp, left to right, top to bottom.")
            Dim freeLayers As New List(Of Integer)()
            For number As Integer = 256 To 1 Step -1
                If number <> part.Layers.WorkLayer AndAlso part.Layers.GetAllObjectsOnLayer(number).Length = 0 Then freeLayers.Add(number)
            Next
            If freeLayers.Count < datums.Count Then Throw New InvalidOperationException("Insufficient empty layers to isolate all sectors.")
            mark = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, Title)
            changed = True
            Dim completed As New List(Of Integer)()
            For index As Integer = 0 To datums.Count - 1
                Dim datum As DatumCsys = datums(index)
                stage = "sector " & (index + 1).ToString() & " / " & datum.Name
                Dim center As Point = MakeCenter(part, datum)
                Dim location As Point3d = center.Coordinates
                session.ListingWindow.WriteLine(String.Format(CultureInfo.InvariantCulture,
                    "Sector {0}: CSYS timestamp {1}, sphere center ({2:R}, {3:R}, {4:R}) mm, radius {5:R} mm.",
                    index + 1, datum.Timestamp, location.X, location.Y, location.Z, radius))
                Dim sphere As Body = MakeSphere(part, center, radius)
                Dim localBodies As New List(Of Body)()
                For Each source As Body In bodyList
                    stage = "sector " & (index + 1).ToString() & ", source " & source.JournalIdentifier
                    Dim distance As Double = MinimumDistance(center, source)
                    If distance > radius + Tolerance Then Continue For
                    Dim intersected As Body() = IntersectWithFaceRecovery(part, source, sphere, center, radius, stage)
                    localBodies.AddRange(intersected)
                Next
                sphere.Blank()
                If localBodies.Count = 0 Then
                    session.ListingWindow.WriteLine("Sector " & (index + 1).ToString() & ": no local sheets at the sphere/tolerance; cell remains empty.")
                    Continue For
                End If
                stage = "sectioning sector " & (index + 1).ToString()
                Dim section As SectionCurve = MakeSection(part, localBodies, FindXYPlane(datum))
                Dim curves As New List(Of Curve)()
                For Each entity As NXObject In section.GetEntities()
                    If TypeOf entity Is Curve Then curves.Add(DirectCast(entity, Curve))
                Next
                If curves.Count = 0 Then Throw New InvalidOperationException("Section produced no curves.")
                part.Layers.SetState(freeLayers(index), NXOpen.Layer.State.Selectable)
                For Each curve As Curve In curves
                    curve.Layer = freeLayers(index)
                Next
                section.SetName("SEAL_SECTOR_" & (index + 1).ToString("000"))
                completed.Add(index)
                session.ListingWindow.WriteLine("Sector " & (index + 1).ToString() & ": " & datum.Name &
                    ", timestamp " & datum.Timestamp.ToString() & ", " & curves.Count.ToString() & " curves.")
            Next
            If completed.Count = 0 Then Throw New InvalidOperationException("No sectors contain intersecting sheets.")
            stage = "creating the drawing sheet"
            CreateSheet(part, width, height)
            For Each index As Integer In completed
                stage = "placing drawing sector " & (index + 1).ToString()
                Dim column As Integer = index Mod columns
                Dim row As Integer = index \ columns
                Dim position As New Point3d(gap + radius + column * pitch, height - gap - radius - row * pitch, 0)
                CreateDrawing(part, datums(index), position, freeLayers(index))
            Next
            If session.UpdateManager.DoUpdate(mark) <> 0 Then Throw New InvalidOperationException("NX reported update errors.")
            Dim remainingBodies As New List(Of Tag)()
            For Each existing As Body In part.Bodies
                remainingBodies.Add(existing.Tag)
            Next
            For Each original As Body In bodyList
                If Not remainingBodies.Contains(original.Tag) Then
                    Throw New InvalidOperationException("An original sheet is missing after the batch.")
                End If
            Next
            changed = False
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Created " & completed.Count.ToString() & " sector views. See the Listing Window for the ordered sector list.")
        Catch ex As Exception
            Dim cleanup As String = ""
            If changed Then
                Try
                    session.UndoToMark(mark, Nothing)
                    cleanup = Environment.NewLine & "Batch changes rolled back."
                Catch undoError As Exception
                    cleanup = Environment.NewLine & "Rollback error: " & undoError.Message
                End Try
            End If
            session.ListingWindow.Open()
            Dim nxError As NXException = TryCast(ex, NXException)
            If nxError IsNot Nothing Then session.ListingWindow.WriteLine("NX error code: " & nxError.ErrorCode.ToString())
            session.ListingWindow.WriteLine(stage & Environment.NewLine & ex.ToString() & cleanup)
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, stage & Environment.NewLine & ex.Message & cleanup)
        End Try
    End Sub

    Private Function Members(group As FeatureGroup) As List(Of Feature)
        Dim result As New List(Of Feature)()
        Dim pending As New Queue(Of FeatureGroup)()
        Dim seen As New List(Of Tag)()
        pending.Enqueue(group)
        seen.Add(group.Tag)
        While pending.Count > 0
            Dim items As Feature() = Nothing
            pending.Dequeue().GetMembers(items)
            For Each item As Feature In items
                If item.Suppressed OrElse seen.Contains(item.Tag) Then Continue For
                seen.Add(item.Tag)
                If TypeOf item Is FeatureGroup Then
                    pending.Enqueue(DirectCast(item, FeatureGroup))
                Else
                    result.Add(item)
                End If
            Next
        End While
        Return result
    End Function

    Private Function ChooseGroup(part As Part, prompt As String) As FeatureGroup
        Dim groups As New List(Of FeatureGroup)()
        For Each feature As Feature In part.Features
            If TypeOf feature Is FeatureGroup AndAlso Not feature.Suppressed Then groups.Add(DirectCast(feature, FeatureGroup))
        Next
        groups.Sort(Function(left, right) left.Timestamp.CompareTo(right.Timestamp))
        If groups.Count = 0 Then Throw New InvalidOperationException("No feature groups found.")
        Using dialog As New Form()
            dialog.Text = prompt
            dialog.Width = 600
            dialog.Height = 165
            dialog.StartPosition = FormStartPosition.CenterParent
            Dim choices As New ComboBox With {.Left = 15, .Top = 15, .Width = 550, .DropDownStyle = ComboBoxStyle.DropDownList}
            For Each group As FeatureGroup In groups
                choices.Items.Add(group.Name & " [timestamp " & group.Timestamp.ToString() & "]")
            Next
            choices.SelectedIndex = 0
            Dim ok As New Button With {.Text = "Select group", .Left = 330, .Top = 60, .Width = 110, .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.Text = "Cancel", .Left = 455, .Top = 60, .Width = 110, .DialogResult = DialogResult.Cancel}
            dialog.Controls.AddRange(New Control() {choices, ok, cancel})
            dialog.AcceptButton = ok
            dialog.CancelButton = cancel
            NXOpenUI.FormUtilities.ReparentForm(dialog)
            If dialog.ShowDialog() <> DialogResult.OK Then Return Nothing
            Return groups(choices.SelectedIndex)
        End Using
    End Function

    Private Function ReadNumber(prompt As String, defaultText As String, allowZero As Boolean, ByRef value As Double) As Boolean
        Do
            Dim answer As String = Interaction.InputBox(prompt, Title, defaultText)
            If String.IsNullOrWhiteSpace(answer) Then Return False
            If Double.TryParse(answer, NumberStyles.Float, CultureInfo.CurrentCulture, value) AndAlso
                Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value) AndAlso
                (value > 0 OrElse (allowZero AndAlso value = 0)) Then Return True
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning, "Enter a valid dimension.")
        Loop
    End Function

    Private Function MakeCenter(part As Part, datum As DatumCsys) As Point
        For Each entity As NXObject In datum.GetEntities()
            If TypeOf entity Is CartesianCoordinateSystem Then
                Dim zero As Scalar = part.Scalars.CreateScalar(0, Scalar.DimensionalityType.Length, SmartObject.UpdateOption.WithinModeling)
                Return part.Points.CreatePoint(DirectCast(entity, CartesianCoordinateSystem), zero, zero, zero, SmartObject.UpdateOption.WithinModeling)
            End If
        Next
        Throw New InvalidOperationException("Datum has no Cartesian coordinate system.")
    End Function

    Private Function MakeSphere(part As Part, center As Point, radius As Double) As Body
        Dim builder As SphereBuilder = part.Features.CreateSphereBuilder(Nothing)
        Try
            builder.Type = SphereBuilder.Types.CenterPointAndDiameter
            builder.CenterPoint = center
            builder.ParentAssociativity = True
            builder.Diameter.RightHandSide = (2 * radius).ToString("R", CultureInfo.InvariantCulture)
            Dim result As Sphere = DirectCast(builder.CommitFeature(), Sphere)
            Dim body As Body = result.GetBodies()(0)
            VerifySphere(body, center.Coordinates, radius)
            Return body
        Finally
            builder.Destroy()
        End Try
    End Function

    Private Sub VerifySphere(body As Body, center As Point3d, expectedRadius As Double)
        If Not body.IsSolidBody Then Throw New InvalidOperationException("The crop sphere is not solid.")
        Dim faces As Face() = body.GetFaces()
        If faces.Length = 0 Then Throw New InvalidOperationException("The crop sphere has no faces.")
        For Each face As Face In faces
            Dim faceType As Integer
            Dim position(2), direction(2), box(5) As Double
            Dim radius, radius2 As Double
            Dim sense As Integer
            UFSession.GetUFSession().Modl.AskFaceData(face.Tag, faceType, position, direction, box, radius, radius2, sense)
            ' UF surface type 18 is an analytic sphere; its position is the sphere center.
            Dim centerError As Double = Math.Sqrt((position(0) - center.X) ^ 2 +
                (position(1) - center.Y) ^ 2 + (position(2) - center.Z) ^ 2)
            If faceType <> 18 OrElse centerError > Tolerance OrElse Math.Abs(radius - expectedRadius) > Tolerance Then
                Throw New InvalidOperationException("Created sphere geometry does not match the requested center/radius.")
            End If
        Next
        NXOpen.Session.GetSession().ListingWindow.WriteLine("Sphere geometry verified against the requested center and radius.")
    End Sub

    Private Function MinimumDistance(center As Point, target As NXObject) As Double
        Dim accuracy As Double
        Dim distance As Double = MeasureMinimumDistance(center, target, accuracy)
        ' Use a conservative lower bound for exclusion, including NX's reported uncertainty.
        Return Math.Max(0, distance - accuracy)
    End Function

    Private Function MeasureMinimumDistance(center As Point, target As NXObject, ByRef accuracy As Double) As Double
        Dim distance As Double
        Dim first(2), second(2), guess1(2), guess2(2) As Double
        UFSession.GetUFSession().Modl.AskMinimumDist3(2, center.Tag, target.Tag, 0, guess1, 0, guess2,
                                                     distance, first, second, accuracy)
        If Double.IsNaN(distance) OrElse Double.IsInfinity(distance) OrElse distance < 0 OrElse
            Double.IsNaN(accuracy) OrElse Double.IsInfinity(accuracy) OrElse accuracy < 0 Then
            Throw New InvalidOperationException("NX returned an invalid minimum distance for " & target.JournalIdentifier)
        End If
        Return distance
    End Function

    Private Function IntersectWithFaceRecovery(part As Part, source As Body, sphere As Body,
                                               center As Point, radius As Double, context As String) As Body()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim attempt As Session.UndoMarkId = session.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Try whole sheet intersect")
        Try
            Return IntersectSheet(part, source, sphere, center, radius, context)
        Catch ex As NXException
            ' IntersectSheet destroys its builder before control reaches this handler.
            ' A failed Commit may leave partial work: remove it before trying copies.
            session.UndoToMark(attempt, Nothing)
            session.ListingWindow.WriteLine(context & ": whole-sheet Intersect failed (NX " &
                ex.ErrorCode.ToString() & "): " & ex.Message)
            session.ListingWindow.WriteLine("Retrying nearby faces individually; radius and 0.01 mm tolerance unchanged.")
        Finally
            session.DeleteUndoMark(attempt, Nothing)
        End Try

        Dim faces As Face() = source.GetFaces()
        If faces.Length = 0 Then Throw New InvalidOperationException(context & ": source sheet has no faces to inspect.")
        Dim output As New List(Of Body)()
        Dim outside As Integer = 0
        Dim processed As Integer = 0
        Dim empty As Integer = 0
        For index As Integer = 0 To faces.Length - 1
            Dim face As Face = faces(index)
            Dim faceContext As String = context & ", face " & (index + 1).ToString() & "/" &
                faces.Length.ToString() & " [tag " & face.Tag.ToString() & ", " & face.JournalIdentifier & "]"
            Try
                ' Proximity is a candidate filter, not proof that a Boolean will succeed.
                If MinimumDistance(center, face) > radius + Tolerance Then
                    outside += 1
                    Continue For
                End If
                session.ListingWindow.WriteLine(faceContext & ": extracting and intersecting.")
                Dim faceMark As Session.UndoMarkId = session.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Try extracted face")
                Try
                    Dim copy As Body = ExtractSingleFace(part, face, source)
                    Dim cropped As Body()
                    Try
                        cropped = IntersectSheet(part, copy, sphere, center, radius, faceContext)
                    Catch ex As NXException When ex.ErrorCode = NoIntersectionError OrElse ex.ErrorCode = IntersectionGapError
                        ' IntersectSheet has undone the failed Boolean. The associative copy still exists.
                        session.ListingWindow.WriteLine(faceContext & ": retrying with Split Body on the extracted copy (NX " & ex.ErrorCode.ToString() & ").")
                        cropped = SplitFaceInsideSphere(part, copy, source, sphere, center, radius, faceContext)
                    End Try
                    If cropped.Length = 0 Then
                        ' Also remove the unused associative extract, retaining earlier successful faces.
                        session.UndoToMark(faceMark, Nothing)
                        empty += 1
                    Else
                        output.AddRange(cropped)
                        processed += 1
                    End If
                Finally
                    session.DeleteUndoMark(faceMark, Nothing)
                End Try
            Catch ex As Exception
                Dim nxError As NXException = TryCast(ex, NXException)
                Dim code As String = ""
                If nxError IsNot Nothing Then code = " (NX " & nxError.ErrorCode.ToString() & ")"
                Throw New InvalidOperationException(faceContext & code & ": " & ex.Message &
                    Environment.NewLine & "Face recovery could not complete. This face has NOT been classified as outside; no partial drawing will be kept.", ex)
            End Try
        Next
        session.ListingWindow.WriteLine(context & ": recovered " & processed.ToString() &
            " faces; " & outside.ToString() & " faces excluded by distance; " & empty.ToString() &
            " confirmed empty intersections; " & output.Count.ToString() & " local sheet bodies.")
        Return output.ToArray()
    End Function

    Private Function SplitFaceInsideSphere(part As Part, copy As Body, original As Body, sphere As Body,
                                           center As Point, radius As Double, context As String) As Body()
        If copy.Tag = original.Tag OrElse copy.Tag = sphere.Tag OrElse Not copy.IsSheetBody Then
            Throw New InvalidOperationException("Split recovery requires a separate extracted sheet copy.")
        End If
        ' No boundary split is needed when the exact bounding box proves complete containment.
        ' This is sufficient, not necessary: uncertain boxes proceed to Split Body.
        If BoundingBoxInsideSphere(copy, center.Coordinates, radius - Tolerance) Then
            NXOpen.Session.GetSession().ListingWindow.WriteLine(context & ": extracted face is wholly inside the sphere; retaining its associative copy.")
            Return New Body() {copy}
        End If

        Dim builder As SplitBodyBuilder = part.Features.CreateSplitBodyBuilderUsingCollector(Nothing)
        Dim result As SplitBody
        Try
            builder.Tolerance = Tolerance
            builder.KeepImprintedEdges = False
            Dim targets As ScCollector = part.ScCollectors.CreateCollector()
            Dim options As SelectionIntentRuleOptions = part.ScRuleFactory.CreateRuleOptions()
            Try
                options.SetSelectedFromInactive(False)
                Dim targetRule As BodyDumbRule = part.ScRuleFactory.CreateRuleBodyDumb(New Body() {copy}, True, options)
                targets.ReplaceRules(New SelectionIntentRule() {targetRule}, False)
                builder.TargetBodyCollector = targets
                builder.BooleanTool.ToolOption = NXOpen.GeometricUtilities.BooleanToolBuilder.BooleanToolType.FaceOrPlane
                Dim tools As ScCollector = builder.BooleanTool.FacePlaneTool.ToolFaces.FaceCollector
                If tools Is Nothing Then Throw New InvalidOperationException("NX did not initialize the Split Body tool-face collector.")
                Dim toolRule As FaceDumbRule = part.ScRuleFactory.CreateRuleFaceDumb(sphere.GetFaces(), options)
                tools.ReplaceRules(New SelectionIntentRule() {toolRule}, False)
            Finally
                options.Dispose()
            End Try
            If Not builder.Validate() Then Throw New InvalidOperationException("NX rejected the Split Body recovery settings.")
            result = TryCast(builder.CommitFeature(), SplitBody)
            If result Is Nothing Then Throw New InvalidOperationException("Split Body returned no feature.")
        Finally
            builder.Destroy()
        End Try
        If result.GetFeatureErrorMessages().Length <> 0 OrElse result.GetFeatureWarningMessages().Length <> 0 Then
            Throw New InvalidOperationException("Split Body reported feature errors/warnings; the result requires inspection.")
        End If
        Dim pieces As Body() = result.GetBodies()
        If pieces.Length < 2 Then Throw New InvalidOperationException("Split Body did not produce separate pieces. The unsplit face will not be accepted as cropped.")
        Dim inside As New List(Of Body)()
        Dim outside As Integer = 0
        For Each piece As Body In pieces
            If piece.Tag = original.Tag OrElse piece.Tag = sphere.Tag OrElse Not piece.IsSheetBody Then
                Throw New InvalidOperationException("Split Body returned an unexpected body.")
            End If
            piece.Blank()
            Dim accuracy As Double
            Dim distance As Double = MeasureMinimumDistance(center, piece, accuracy)
            If accuracy > Tolerance Then Throw New InvalidOperationException("A split piece cannot be classified accurately enough.")
            ' A successful closed-sphere split partitions the sheet at the sphere surface.
            ' Classify each resulting piece, not the unsplit source. Retain every interior piece.
            If distance + accuracy < radius - Tolerance Then
                inside.Add(piece)
            ElseIf distance - accuracy >= radius - Tolerance Then
                outside += 1
            Else
                Throw New InvalidOperationException("Split piece classification is ambiguous near the sphere boundary.")
            End If
        Next
        If inside.Count = 0 Then Throw New InvalidOperationException("Split recovery found no interior piece despite the earlier inside measurement.")
        result.SetName("SEAL_CROP_SPLIT_RECOVERY")
        NXOpen.Session.GetSession().ListingWindow.WriteLine(context & ": Split Body recovered " & inside.Count.ToString() &
            " interior pieces; " & outside.ToString() & " exterior/boundary pieces excluded from sectioning.")
        Return inside.ToArray()
    End Function

    Private Function BoundingBoxInsideSphere(body As Body, center As Point3d, radius As Double) As Boolean
        If radius <= 0 Then Return False
        Dim corner(2), lengths(2) As Double
        Dim directions(2, 2) As Double
        UFSession.GetUFSession().Modl.AskBoundingBoxExact(body.Tag, Tag.Null, corner, directions, lengths)
        For mask As Integer = 0 To 7
            Dim point As Double() = DirectCast(corner.Clone(), Double())
            For axis As Integer = 0 To 2
                If (mask And (1 << axis)) = 0 Then Continue For
                For component As Integer = 0 To 2
                    point(component) += lengths(axis) * directions(axis, component)
                Next
            Next
            Dim distanceSquared As Double = (point(0) - center.X) ^ 2 + (point(1) - center.Y) ^ 2 + (point(2) - center.Z) ^ 2
            If Double.IsNaN(distanceSquared) OrElse Double.IsInfinity(distanceSquared) OrElse distanceSquared >= radius * radius Then Return False
        Next
        Return True
    End Function

    Private Function ExtractSingleFace(part As Part, face As Face, source As Body) As Body
        Dim builder As ExtractFaceBuilder = part.Features.CreateExtractFaceBuilder(Nothing)
        Try
            builder.Type = ExtractFaceBuilder.ExtractType.Face
            builder.FaceOption = ExtractFaceBuilder.FaceOptionType.SingleFace
            builder.ParentPart = ExtractFaceBuilder.ParentPartType.WorkPart
            builder.Associative = True
            builder.HideOriginal = False
            If Not builder.ObjectToExtract.Add(face) Then Throw New InvalidOperationException("NX rejected the face for extraction.")
            Dim feature As Feature = builder.CommitFeature()
            If feature Is Nothing Then Throw New InvalidOperationException("Extract Face returned no feature.")
            Dim bodies As Body() = feature.GetBodies()
            If bodies.Length <> 1 Then Throw New InvalidOperationException("Single-face extraction did not return exactly one body.")
            If bodies(0).Tag = source.Tag OrElse Not bodies(0).IsSheetBody Then
                Throw New InvalidOperationException("Extract Face did not return a separate sheet copy.")
            End If
            bodies(0).Blank()
            Return bodies(0)
        Finally
            builder.Destroy()
        End Try
    End Function

    Private Function IntersectSheet(part As Part, source As Body, sphere As Body,
                                    center As Point, radius As Double, context As String) As Body()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim attempt As Session.UndoMarkId = session.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Intersect attempt")
        Try
            Return CommitSheetIntersection(part, source, sphere)
        Catch ex As NXException
            ' Restore the target before measuring it. The builder has already been destroyed.
            session.UndoToMark(attempt, Nothing)
            If ex.ErrorCode = NoIntersectionError Then
                Dim accuracy As Double
                Dim distance As Double = MeasureMinimumDistance(center, source, accuracy)
                session.ListingWindow.WriteLine(String.Format(CultureInfo.InvariantCulture,
                    "{0}: NX 670030; rechecked center distance {1:R} mm, accuracy {2:R} mm, radius {3:R} mm.",
                    context, distance, accuracy, radius))
                ' Accept NX's empty result only if a reliable recheck agrees to modeling tolerance.
                If accuracy <= Tolerance AndAlso distance - accuracy >= radius - Tolerance Then
                    session.ListingWindow.WriteLine(context & ": confirmed empty at 0.01 mm tolerance; continuing.")
                    Return New Body() {}
                End If
                session.ListingWindow.WriteLine(context & ": proximity does not confirm an empty intersection; preserving the failure for recovery/diagnosis.")
            End If
            Throw
        Finally
            session.DeleteUndoMark(attempt, Nothing)
        End Try
    End Function

    Private Function CommitSheetIntersection(part As Part, source As Body, sphere As Body) As Body()
        Dim builder As BooleanBuilder = part.Features.CreateBooleanBuilderUsingCollector(Nothing)
        Try
            builder.Operation = Feature.BooleanType.Intersect
            builder.CopyTargets = True
            builder.CopyTools = True
            builder.Tolerance = Tolerance
            ' New Boolean builders may have no target/tool collectors yet.
            ' Create and assign both explicitly, as in intersectSheet.vb.
            Dim targetCollector As ScCollector = part.ScCollectors.CreateCollector()
            Dim toolCollector As ScCollector = part.ScCollectors.CreateCollector()
            Dim options As SelectionIntentRuleOptions = part.ScRuleFactory.CreateRuleOptions()
            Try
                options.SetSelectedFromInactive(False)
                Dim targetRule As BodyDumbRule = part.ScRuleFactory.CreateRuleBodyDumb(New Body() {source}, True, options)
                Dim toolRule As BodyDumbRule = part.ScRuleFactory.CreateRuleBodyDumb(New Body() {sphere}, True, options)
                targetCollector.ReplaceRules(New SelectionIntentRule() {targetRule}, False)
                toolCollector.ReplaceRules(New SelectionIntentRule() {toolRule}, False)
            Finally
                options.Dispose()
            End Try
            builder.TargetBodyCollector = targetCollector
            builder.ToolBodyCollector = toolCollector
            Dim result As BooleanFeature = TryCast(builder.Commit(), BooleanFeature)
            If result Is Nothing Then Throw New InvalidOperationException("Intersect returned no feature.")
            Dim output As Body() = result.GetBodies()
            If output.Length = 0 Then Throw New InvalidOperationException("Intersect returned no bodies.")
            For Each body As Body In output
                If body.Tag = source.Tag OrElse body.Tag = sphere.Tag OrElse Not body.IsSheetBody Then
                    Throw New InvalidOperationException("Intersect did not return separate sheet output.")
                End If
                body.Blank()
            Next
            Return output
        Finally
            builder.Destroy()
        End Try
    End Function

    Private Function MakeSection(part As Part, bodies As List(Of Body), plane As DatumPlane) As SectionCurve
        Dim builder As SectionCurveBuilder = part.Features.CreateSectionCurveBuilder(Nothing)
        Try
            builder.Type = SectionCurveBuilder.PlaneType.Selected
            builder.Associative = True
            builder.OutputPoints = False
            For Each body As Body In bodies
                If Not builder.ObjectsToSection.Add(body) Then Throw New InvalidOperationException("Section rejected a body.")
            Next
            If Not builder.SectionPlanes.Add(plane) Then Throw New InvalidOperationException("Section rejected its plane.")
            Return DirectCast(builder.CommitFeature(), SectionCurve)
        Finally
            builder.Destroy()
        End Try
    End Function

    Private Sub CreateDrawing(part As Part, datum As DatumCsys, position As Point3d, sectionLayer As Integer)
        Dim session As Session = NXOpen.Session.GetSession()
        Dim csys As CartesianCoordinateSystem = Nothing
        For Each entity As NXObject In datum.GetEntities()
            If TypeOf entity Is CartesianCoordinateSystem Then
                If csys IsNot Nothing Then Throw New InvalidOperationException("The Datum CSYS has multiple coordinate systems.")
                csys = DirectCast(entity, CartesianCoordinateSystem)
            End If
        Next
        If csys Is Nothing Then Throw New InvalidOperationException("No Cartesian system found in the Datum CSYS.")
        Dim x As Vector3d
        Dim y As Vector3d
        csys.GetDirections(x, y)
        Dim z As New Vector3d(x.Y * y.Z - x.Z * y.Y, x.Z * y.X - x.X * y.Z, x.X * y.Y - x.Y * y.X)
        Dim xy As DatumPlane = Nothing
        Dim xAxis As DatumAxis = Nothing
        Dim xSense As Sense = Sense.Forward
        Dim zSense As Sense = Sense.Forward
        For Each entity As NXObject In datum.GetEntities()
            Dim candidatePlane As DatumPlane = TryCast(entity, DatumPlane)
            If candidatePlane IsNot Nothing Then
                Dim n As Vector3d = candidatePlane.Normal
                Dim dot As Double = n.X * z.X + n.Y * z.Y + n.Z * z.Z
                If Math.Abs(dot) > 0.999999 Then
                    xy = candidatePlane
                    If dot < 0 Then zSense = Sense.Reverse
                End If
            End If
            Dim candidateAxis As DatumAxis = TryCast(entity, DatumAxis)
            If candidateAxis IsNot Nothing Then
                Dim d As Vector3d = candidateAxis.Direction
                Dim dot As Double = d.X * x.X + d.Y * x.Y + d.Z * x.Z
                If Math.Abs(dot) > 0.999999 Then
                    xAxis = candidateAxis
                    If dot < 0 Then xSense = Sense.Reverse
                End If
            End If
        Next
        If xy Is Nothing OrElse xAxis Is Nothing Then Throw New InvalidOperationException("Cannot find XY plane and X axis in the selected Datum CSYS.")
        Dim builder As NXOpen.Drawings.BaseViewBuilder = part.DraftingViews.CreateBaseViewBuilder(Nothing)
        Try
            builder.Placement.Associative = True
            builder.Scale.ScaleType = NXOpen.Drawings.ViewScaleBuilder.Type.Ratio
            builder.Scale.Numerator = 1.0
            builder.Scale.Denominator = 1.0
            builder.SelectModelView.SelectedView = DirectCast(part.ModelingViews.FindObject("Top"), ModelingView)
            builder.SecondaryComponents.ObjectType = NXOpen.Drawings.DraftingComponentSelectionBuilder.Geometry.PrimaryGeometry
            builder.Style.ViewStyleBase.Part = part
            builder.Style.ViewStyleHiddenLines.IncludeModelCurvesOption = NXOpen.Preferences.IncludeModelCurvesOption.Yes
            builder.Style.ViewStyleOrientation.Ovt.NormalDirection =
                part.Directions.CreateDirection(xy, zSense, SmartObject.UpdateOption.AfterModeling)
            builder.Style.ViewStyleOrientation.Ovt.XDirection =
                part.Directions.CreateDirection(xAxis, xSense, SmartObject.UpdateOption.AfterModeling)
            builder.Placement.Placement.SetValue(Nothing, part.Views.WorkView,
                                position)
            If Not builder.Validate() Then Throw New InvalidOperationException("NX rejected the base-view settings.")
            Dim view As NXOpen.Drawings.BaseView = TryCast(builder.Commit(), NXOpen.Drawings.BaseView)
            If view Is Nothing Then Throw New InvalidOperationException("NX did not create a base view.")
            Dim states(255) As NXOpen.Layer.StateInfo
            For layerNumber As Integer = 1 To 256
                Dim visibility As NXOpen.Layer.State = NXOpen.Layer.State.Hidden
                If layerNumber = sectionLayer Then visibility = NXOpen.Layer.State.Visible
                states(layerNumber - 1) = New NXOpen.Layer.StateInfo(layerNumber, visibility)
            Next
            part.Layers.SetObjectsVisibilityOnLayer(view, states, True)
            Dim modelPoint As Double() = {csys.Origin.X, csys.Origin.Y, csys.Origin.Z}
            ' UF expects two drawing coordinates; VB uses an inclusive upper bound.
            Dim drawingPoint(1) As Double
            UFSession.GetUFSession().View.MapModelToDrawing(view.Tag, modelPoint, drawingPoint)
            Dim reference As Point3d = view.GetDrawingReferencePoint()
            view.MoveView(New Point3d(reference.X + position.X - drawingPoint(0),
                                     reference.Y + position.Y - drawingPoint(1), 0))
            view.SetName("SECTOR_" & datum.Timestamp.ToString())
        Finally
            builder.Destroy()
        End Try
    End Sub

    Private Sub CreateSheet(part As Part, sheetWidth As Double, sheetHeight As Double)
        NXOpen.Session.GetSession().ApplicationSwitchImmediate("UG_APP_DRAFTING")
        part.Drafting.EnterDraftingApplication()
        Dim sheetBuilder As NXOpen.Drawings.DraftingDrawingSheetBuilder =
            part.DraftingDrawingSheets.CreateDraftingDrawingSheetBuilder(Nothing)
        Dim sheet As NXOpen.Drawings.DraftingDrawingSheet
        Try
            sheetBuilder.AutoStartViewCreation = False
            sheetBuilder.Option = NXOpen.Drawings.DrawingSheetBuilder.SheetOption.CustomSize
            sheetBuilder.Height = sheetHeight
            sheetBuilder.Length = sheetWidth
            sheetBuilder.ScaleNumerator = 1.0
            sheetBuilder.ScaleDenominator = 1.0
            sheetBuilder.Units = NXOpen.Drawings.DrawingSheetBuilder.SheetUnits.Metric
            sheetBuilder.ProjectionAngle = NXOpen.Drawings.DrawingSheetBuilder.SheetProjectionAngle.Third
            sheet = DirectCast(sheetBuilder.Commit(), NXOpen.Drawings.DraftingDrawingSheet)
        Finally
            sheetBuilder.Destroy()
        End Try
        sheet.Open()
    End Sub

    Private Function FindXYPlane(datum As DatumCsys) As DatumPlane
        Dim entities As NXObject() = datum.GetEntities()
        Dim csys As CartesianCoordinateSystem = Nothing
        For Each entity As NXObject In entities
            If TypeOf entity Is CartesianCoordinateSystem Then
                If csys IsNot Nothing Then Throw New InvalidOperationException("Datum CSYS has multiple Cartesian entities.")
                csys = DirectCast(entity, CartesianCoordinateSystem)
            End If
        Next
        If csys Is Nothing Then Throw New InvalidOperationException("No Cartesian entity was found in the Datum CSYS.")

        Dim x As Vector3d
        Dim y As Vector3d
        csys.GetDirections(x, y)
        Dim z As New Vector3d(x.Y * y.Z - x.Z * y.Y,
                              x.Z * y.X - x.X * y.Z,
                              x.X * y.Y - x.Y * y.X)
        Dim xy As DatumPlane = Nothing
        For Each entity As NXObject In entities
            Dim plane As DatumPlane = TryCast(entity, DatumPlane)
            If plane Is Nothing Then Continue For
            Dim n As Vector3d = plane.Normal
            Dim dot As Double = n.X * z.X + n.Y * z.Y + n.Z * z.Z
            If Math.Abs(dot) < 0.999999 Then Continue For
            If xy IsNot Nothing Then Throw New InvalidOperationException("Datum CSYS has ambiguous XY planes.")
            xy = plane
        Next
        If xy Is Nothing Then Throw New InvalidOperationException("The Datum CSYS has no selectable XY datum plane.")
        Return xy
    End Function
End Module
