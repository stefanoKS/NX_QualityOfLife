Option Strict On
Imports System
Imports System.Globalization
Imports Microsoft.VisualBasic
Imports NXOpen
Imports NXOpen.Features

Module SealSectionDraftTest
    Private Const Title As String = "Seal Section Draft Test"

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim ui As UI = NXOpen.UI.GetUI()
        Dim part As Part = session.Parts.Work
        Try
            If part Is Nothing Then Throw New InvalidOperationException("Open the work part containing the sector CSYS.")
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Select the sector Datum CSYS or one of its datum planes. " &
                "A new drawing sheet and oriented base view will be created. Hide other model geometry first.")

            Dim selected As TaggedObject = Nothing
            Dim cursor As Point3d
            Dim types As Selection.SelectionType() = {Selection.SelectionType.All}
            Dim response As Selection.Response = ui.SelectionManager.SelectTaggedObject(
                "Select the sector Datum CSYS or one of its datum planes.", Title,
                Selection.SelectionScope.WorkPart, False, types, selected, cursor)
            If response <> Selection.Response.ObjectSelected AndAlso
                response <> Selection.Response.ObjectSelectedByName Then Return

            Dim obj As NXObject = TryCast(selected, NXObject)
            If obj Is Nothing OrElse obj.IsOccurrence OrElse obj.OwningPart.Tag <> part.Tag Then
                Throw New InvalidOperationException("Select a Datum CSYS in the work part.")
            End If
            Dim datum As DatumCsys = TryCast(obj, DatumCsys)
            Dim plane As DatumPlane = TryCast(obj, DatumPlane)
            If plane IsNot Nothing Then datum = TryCast(plane.Feature, DatumCsys)
            If TypeOf obj Is CartesianCoordinateSystem Then
                For Each feature As Feature In part.Features
                    Dim candidate As DatumCsys = TryCast(feature, DatumCsys)
                    If candidate Is Nothing OrElse candidate.Suppressed Then Continue For
                    For Each entity As NXObject In candidate.GetEntities()
                        If entity.Tag = obj.Tag Then datum = candidate
                    Next
                    If datum IsNot Nothing Then Exit For
                Next
            End If
            If datum Is Nothing Then Throw New InvalidOperationException("Select a Datum CSYS or one of its datum planes.")

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
            If part.PartUnits <> BasePart.Units.Millimeters Then Throw New InvalidOperationException("This test uses a metric sheet. Open a millimeter part.")
            Dim sheetWidth As Double
            Dim sheetHeight As Double
            If Not ReadDimension("sheet width", sheetWidth) Then Return
            If Not ReadDimension("sheet height", sheetHeight) Then Return
            Dim mark As Session.UndoMarkId = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, Title)
            Try
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
                    If builder.Commit() Is Nothing Then Throw New InvalidOperationException("NX did not create a base view.")
                Finally
                    builder.Destroy()
                End Try
            Catch
                session.UndoToMark(mark, Nothing)
                Throw
            End Try
        Catch ex As Exception
            session.ListingWindow.Open()
            session.ListingWindow.WriteLine(ex.ToString())
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ex.Message)
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
End Module
