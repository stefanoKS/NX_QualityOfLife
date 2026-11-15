Option Strict On
Imports System
Imports NXOpen
Imports NXOpen.Features

Module SealSectionPoC01A
    Private Const Title As String = "Seal Section PoC 01A"

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim ui As UI = NXOpen.UI.GetUI()
        Dim part As Part = session.Parts.Work
        Dim stage As String = "checking the work part"
        Dim mark As Session.UndoMarkId
        Dim hasMark As Boolean = False

        Try
            If part Is Nothing Then Throw New InvalidOperationException("Open a modeling work part.")

            ui.NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Select in this order:" & Environment.NewLine &
                "1. Click the panel body or one of its faces." & Environment.NewLine &
                "2. Click the sector Datum CSYS or one of its datum planes." & Environment.NewLine &
                "The Name box may be left empty when selecting in the graphics window.")

            stage = "selecting the panel body"
            Dim picked As NXObject = Pick("Step 1 of 2: Select the panel body or one of its faces.")
            If picked Is Nothing Then Return
            Dim body As Body = TryCast(picked, Body)
            Dim face As Face = TryCast(picked, Face)
            If face IsNot Nothing Then body = face.GetBody()
            If body Is Nothing OrElse body.IsOccurrence OrElse body.OwningPart.Tag <> part.Tag OrElse
                Not (body.IsSolidBody OrElse body.IsSheetBody) Then
                Throw New InvalidOperationException("Select a solid or sheet body in the work part.")
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

            stage = "creating the NX 2306 Section Curve builder"
            mark = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, Title)
            hasMark = True
            Dim builder As SectionCurveBuilder = part.Features.CreateSectionCurveBuilder(Nothing)
            Dim curveCount As Integer = 0
            Try
                stage = "setting the associative body and selected datum plane"
                builder.Type = SectionCurveBuilder.PlaneType.Selected
                builder.Associative = True
                builder.OutputPoints = False
                If Not builder.ObjectsToSection.Add(body) Then
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
                Dim section As SectionCurve = TryCast(builder.CommitFeature(), SectionCurve)
                If section Is Nothing Then Throw New InvalidOperationException("NX returned no Section Curve feature.")
                For Each entity As NXObject In section.GetEntities()
                    If TypeOf entity Is Curve Then curveCount += 1
                Next
                If curveCount = 0 Then
                    Throw New InvalidOperationException("No curves were created. Check that the sector plane crosses the panel.")
                End If
                section.SetName("SEAL_SECTION_01A")
            Finally
                builder.Destroy()
            End Try
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
