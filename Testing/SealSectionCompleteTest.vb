Option Strict On
Imports System
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.UF
Imports System.Globalization
Imports Microsoft.VisualBasic

Module SealSectionCompleteTest
    Private Const Title As String = "Seal Section Complete Test"

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim ui As UI = NXOpen.UI.GetUI()
        Dim part As Part = session.Parts.Work
        Dim stage As String = "checking the work part"
        Dim mark As Session.UndoMarkId
        Dim hasMark As Boolean = False

        Try
            If part Is Nothing Then Throw New InvalidOperationException("Open a modeling work part.")
            If session.Parts.Display Is Nothing OrElse session.Parts.Display.Tag <> part.Tag Then
                Throw New InvalidOperationException("Make the work part the displayed part before running this journal.")
            End If

            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Select in this order:" & Environment.NewLine &
                "1. Click the panel body or one of its faces." & Environment.NewLine &
                "2. Click the sector Datum CSYS or one of its datum planes." & Environment.NewLine &
                "3. Enter the sphere radius and sheet dimensions in millimeters." & Environment.NewLine &
                "The Name box may be left empty when selecting in the graphics window.")

            stage = "selecting the panel body"
            Dim picked As NXObject = Pick("Step 1 of 2: Select the panel body or one of its faces.")
            If picked Is Nothing Then Return
            Dim body As Body = TryCast(picked, Body)
            Dim face As Face = TryCast(picked, Face)
            If face IsNot Nothing Then body = face.GetBody()
            If body Is Nothing OrElse body.IsOccurrence OrElse body.OwningPart.Tag <> part.Tag OrElse
                Not body.IsSheetBody Then
                Throw New InvalidOperationException("Select a sheet body in the work part.")
            End If

            stage = "resolving the Datum CSYS XY plane"
            picked = Pick("Step 2 of 2: Select the sector Datum CSYS or one of its displayed datum planes.")
            If picked Is Nothing Then Return
            If picked.IsOccurrence OrElse picked.OwningPart.Tag <> part.Tag Then
                Throw New InvalidOperationException("Select a Datum CSYS in the work part.")
            End If
            Dim datum As DatumCsys = TryCast(picked, DatumCsys)
            Dim pickedPlane As DatumPlane = TryCast(picked, DatumPlane)
            If pickedPlane IsNot Nothing Then datum = TryCast(pickedPlane.Feature, DatumCsys)
            If TypeOf picked Is CartesianCoordinateSystem Then
                For Each feature As Feature In part.Features
                    Dim candidate As DatumCsys = TryCast(feature, DatumCsys)
                    If candidate Is Nothing OrElse candidate.Suppressed Then Continue For
                    For Each entity As NXObject In candidate.GetEntities()
                        If entity.Tag = picked.Tag Then datum = candidate
                    Next
                    If datum IsNot Nothing Then Exit For
                Next
            End If
            If datum Is Nothing Then
                Throw New InvalidOperationException("Pick the sector Datum CSYS or a datum plane belonging to it.")
            End If
            Dim xy As DatumPlane = FindXYPlane(datum)

            Dim radiusText As String = Interaction.InputBox(
                "Enter a positive sphere radius in the work part's units.", Title, "")
            If String.IsNullOrWhiteSpace(radiusText) Then Return
            Dim radius As Double
            If Not Double.TryParse(radiusText, NumberStyles.Float, CultureInfo.CurrentCulture, radius) OrElse
                Double.IsNaN(radius) OrElse Double.IsInfinity(radius) OrElse radius <= 0 Then
                Throw New InvalidOperationException("Enter a positive numeric radius.")
            End If

            If part.PartUnits <> BasePart.Units.Millimeters Then
                Throw New InvalidOperationException("This test requires a millimeter part.")
            End If
            Dim sheetWidth As Double
            Dim sheetHeight As Double
            If Not ReadDimension("sheet width", sheetWidth) Then Return
            If Not ReadDimension("sheet height", sheetHeight) Then Return

            stage = "creating the sphere and trimming the sheet copy"
            mark = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, Title)
            hasMark = True
            Dim localBody As Body = CreateLocalSheet(part, body, datum, radius)

            stage = "creating the Section Curve builder"
            Dim builder As SectionCurveBuilder = part.Features.CreateSectionCurveBuilder(Nothing)
            Dim curveCount As Integer = 0
            Dim section As SectionCurve = Nothing
            Try
                stage = "setting the associative body and selected datum plane"
                builder.Type = SectionCurveBuilder.PlaneType.Selected
                builder.Associative = True
                builder.OutputPoints = False
                If Not builder.ObjectsToSection.Add(localBody) Then
                    Throw New InvalidOperationException("NX rejected the body input.")
                End If
                If Not builder.SectionPlanes.Add(xy) Then
                    Throw New InvalidOperationException("NX rejected the selected datum plane.")
                End If
                stage = "validating the Section Curve"
                If Not builder.Validate() Then
                    Throw New InvalidOperationException("NX did not validate the Section Curve inputs.")
                End If
                stage = "committing the Section Curve"
                section = TryCast(builder.CommitFeature(), SectionCurve)
                If section Is Nothing Then Throw New InvalidOperationException("NX returned no Section Curve feature.")
                For Each entity As NXObject In section.GetEntities()
                    If TypeOf entity Is Curve Then curveCount += 1
                Next
                If curveCount = 0 Then
                    Throw New InvalidOperationException("No curves were created. Check that the sector plane crosses the panel.")
                End If
                section.SetName("SEAL_SECTION_SHEET_CROP")
            Finally
                builder.Destroy()
            End Try
            stage = "isolating section curves"
            Dim sectionLayer As Integer = FindEmptyVisibleLayer(part)
            For Each entity As NXObject In section.GetEntities()
                Dim curve As Curve = TryCast(entity, Curve)
                If curve IsNot Nothing Then curve.Layer = sectionLayer
            Next
            stage = "creating the drawing sheet and oriented view"
            CreateDrawing(part, datum, sheetWidth, sheetHeight, sectionLayer)
            hasMark = False
            session.ListingWindow.Open()
            session.ListingWindow.WriteLine(Title & " created " & curveCount.ToString() & " curve(s).")
        Catch ex As Exception
            Dim rollback As String = ""
            If hasMark Then
                Try
                    session.UndoToMark(mark, Nothing)
                    session.DeleteUndoMark(mark, Nothing)
                Catch undoError As Exception
                    rollback = Environment.NewLine & "Undo cleanup reported: " & undoError.Message
                End Try
            End If
            session.ListingWindow.Open()
            session.ListingWindow.WriteLine("Failed while " & stage & Environment.NewLine & ex.ToString() & rollback)
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Error,
                                 "Failed while " & stage & Environment.NewLine & ex.Message & rollback)
        End Try
    End Sub

    Private Function FindEmptyVisibleLayer(part As Part) As Integer
        For layerNumber As Integer = 256 To 1 Step -1
            Dim layerState As NXOpen.Layer.State = part.Layers.GetState(layerNumber)
            If (layerState = NXOpen.Layer.State.Visible OrElse layerState = NXOpen.Layer.State.Selectable) AndAlso
                part.Layers.GetAllObjectsOnLayer(layerNumber).Length = 0 Then
                Return layerNumber
            End If
        Next
        Throw New InvalidOperationException("No empty visible or selectable layer is available for the section curves.")
    End Function

    Private Sub CreateDrawing(part As Part, datum As DatumCsys, sheetWidth As Double, sheetHeight As Double, sectionLayer As Integer)
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
        session.ApplicationSwitchImmediate("UG_APP_DRAFTING")
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
        Dim builder As NXOpen.Drawings.BaseViewBuilder = part.DraftingViews.CreateBaseViewBuilder(Nothing)
        Try
            builder.Placement.Associative = True
            builder.SelectModelView.SelectedView = DirectCast(part.ModelingViews.FindObject("Top"), ModelingView)
            builder.SecondaryComponents.ObjectType = NXOpen.Drawings.DraftingComponentSelectionBuilder.Geometry.PrimaryGeometry
            builder.Style.ViewStyleBase.Part = part
            builder.Style.ViewStyleHiddenLines.IncludeModelCurvesOption = NXOpen.Preferences.IncludeModelCurvesOption.Yes
            builder.Style.ViewStyleOrientation.Ovt.NormalDirection =
                part.Directions.CreateDirection(xy, zSense, SmartObject.UpdateOption.AfterModeling)
            builder.Style.ViewStyleOrientation.Ovt.XDirection =
                part.Directions.CreateDirection(xAxis, xSense, SmartObject.UpdateOption.AfterModeling)
            builder.Placement.Placement.SetValue(Nothing, part.Views.WorkView,
                                New Point3d(sheetWidth / 2.0, sheetHeight / 2.0, 0))
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
        Finally
            builder.Destroy()
        End Try
    End Sub

    Private Function ReadDimension(label As String, ByRef value As Double) As Boolean
        Do
            Dim input As String = Interaction.InputBox("Enter " & label & " in millimeters.", Title, "5000")
            If String.IsNullOrWhiteSpace(input) Then Return False
            If Double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, value) AndAlso
                Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value) AndAlso value > 0 Then
                Return True
            End If
            UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning,
                                         "Enter a positive number for the " & label & ".")
        Loop
    End Function

    Private Function CreateLocalSheet(part As Part, body As Body, datum As DatumCsys, radius As Double) As Body
        Dim csys As CartesianCoordinateSystem = Nothing
        For Each entity As NXObject In datum.GetEntities()
            If TypeOf entity Is CartesianCoordinateSystem Then csys = DirectCast(entity, CartesianCoordinateSystem)
        Next
        If csys Is Nothing Then Throw New InvalidOperationException("No Cartesian entity in Datum CSYS.")

        Dim origin As Point3d = csys.Origin
        Dim originPoint As Point = part.Points.CreatePoint(origin)
        Dim distance As Double
        Dim onOrigin(2) As Double
        Dim onSheet(2) As Double
        Dim guess1(2) As Double
        Dim guess2(2) As Double
        UFSession.GetUFSession().Modl.AskMinimumDist(originPoint.Tag, body.Tag, 0, guess1, 0, guess2,
                                                    distance, onOrigin, onSheet)
        If distance >= radius - 0.01 Then
            Throw New InvalidOperationException("The sphere does not contain a sheet point away from its boundary. Increase the radius.")
        End If

        Dim zero As Scalar = part.Scalars.CreateScalar(0.0, Scalar.DimensionalityType.Length,
                                                        SmartObject.UpdateOption.WithinModeling)
        Dim center As Point = part.Points.CreatePoint(csys, zero, zero, zero, SmartObject.UpdateOption.WithinModeling)
        Dim sphereBuilder As SphereBuilder = part.Features.CreateSphereBuilder(Nothing)
        Dim sphere As Sphere
        Try
            sphereBuilder.Type = SphereBuilder.Types.CenterPointAndDiameter
            sphereBuilder.CenterPoint = center
            sphereBuilder.ParentAssociativity = True
            sphereBuilder.Diameter.RightHandSide = (2.0 * radius).ToString("R", CultureInfo.InvariantCulture)
            sphere = DirectCast(sphereBuilder.CommitFeature(), Sphere)
        Finally
            sphereBuilder.Destroy()
        End Try
        Dim sphereBodies As Body() = sphere.GetBodies()
        If sphereBodies.Length <> 1 Then Throw New InvalidOperationException("Sphere did not produce one body.")
        Dim trim As TrimSheetBuilder = part.Features.CreateTrimsheetBuilder(Nothing)
        Dim trimmed As Feature
        Try
            trim.Tolerance = 0.01
            trim.ApplyToCopyOfSheets = True
            trim.ExtendBoundaryObject = True
            If Not trim.TargetBodies.Add(body) Then Throw New InvalidOperationException("NX rejected the sheet.")
            Dim collector As ScCollector = part.ScCollectors.CreateCollector()
            Dim rule As FaceBodyRule = part.ScRuleFactory.CreateRuleFaceBody(sphereBodies(0))
            Dim rules As SelectionIntentRule() = {rule}
            collector.ReplaceRules(rules, False)
            If Not trim.BoundaryObjects.Add(collector) Then Throw New InvalidOperationException("NX rejected the sphere boundary.")
            trim.KeepDiscardMethod = TrimSheetBuilder.KeepDiscardOption.Keep
            Dim regionPoint As Point = part.Points.CreatePoint(New Point3d(onSheet(0), onSheet(1), onSheet(2)))
            trim.Regions.Append(part.CreateRegionPoint(regionPoint, body))
            If Not trim.Validate() Then Throw New InvalidOperationException("NX did not validate the sheet trim.")
            trimmed = trim.CommitFeature()
        Finally
            trim.Destroy()
        End Try
        If trimmed Is Nothing Then Throw New InvalidOperationException("NX returned no trim feature.")
        Dim localBodies As Body() = DirectCast(trimmed, BodyFeature).GetBodies()
        If localBodies.Length <> 1 Then Throw New InvalidOperationException("Trim returned " & localBodies.Length.ToString() & " bodies; expected one.")
        If localBodies(0).Tag = body.Tag Then Throw New InvalidOperationException("Trim did not return a separate copy.")
        sphereBodies(0).Blank()
        localBodies(0).Blank()
        Return localBodies(0)
    End Function

    Private Function Pick(cue As String) As NXObject
        Dim selected As TaggedObject = Nothing
        Dim cursor As Point3d
        Dim types As Selection.SelectionType() = {Selection.SelectionType.All}
        Dim result As Selection.Response = UI.GetUI().SelectionManager.SelectTaggedObject(
            cue, Title, Selection.SelectionScope.WorkPart, False, types, selected, cursor)
        Select Case result
            Case Selection.Response.ObjectSelected, Selection.Response.ObjectSelectedByName
                Dim obj As NXObject = TryCast(selected, NXObject)
                If obj Is Nothing Then Throw New InvalidOperationException("Select a model object.")
                Return obj
            Case Else
                Return Nothing
        End Select
    End Function

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
