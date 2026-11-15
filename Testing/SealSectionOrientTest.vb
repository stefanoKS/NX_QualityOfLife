Option Strict On
Imports System
Imports NXOpen
Imports NXOpen.Features

Module SealSectionOrientTest
    Private Const Title As String = "Seal Section Orientation Test"

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim ui As UI = NXOpen.UI.GetUI()
        Dim part As Part = session.Parts.Work
        Try
            If part Is Nothing Then Throw New InvalidOperationException("Open the work part containing the sector CSYS.")
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Select the sector Datum CSYS or one of its datum planes. " &
                "The modeling view will be aligned with its XY plane, with X to the right and Y upward.")

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
            Dim z As New Vector3d(x.Y * y.Z - x.Z * y.Y,
                                  x.Z * y.X - x.X * y.Z,
                                  x.X * y.Y - x.Y * y.X)
            Dim orientation As New Matrix3x3()
            orientation.Xx = x.X : orientation.Xy = x.Y : orientation.Xz = x.Z
            orientation.Yx = y.X : orientation.Yy = y.Y : orientation.Yz = y.Z
            orientation.Zx = z.X : orientation.Zy = z.Y : orientation.Zz = z.Z
            part.ModelingViews.WorkView.Orient(orientation)
        Catch ex As Exception
            session.ListingWindow.Open()
            session.ListingWindow.WriteLine(ex.ToString())
            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, ex.Message)
        End Try
    End Sub
End Module
