Option Strict On
Imports System
Imports System.Globalization
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.UF

' Read-only investigation of the reported sector-1 failure.
' No model objects, features, undo marks, or files are created or changed.
' Run in the same work part after the failed batch has rolled back. No selections.
Module SealSectionFaceDiagnostic
    Private Const SourceIdentifier As String = "UNPARAMETERIZED_FEATURE(1)"
    Private Const CsysTimestamp As Integer = 111
    Private Const Radius As Double = 60.0
    Private Const Tolerance As Double = 0.01

    Private Class Measurement
        Public Distance As Double
        Public WitnessDistance As Double
        Public Accuracy As Double
        Public Containment As Integer
        Public Point As Double()
    End Class

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        session.ListingWindow.Open()
        Write("=== SEAL FACE DIAGNOSTIC v1 (READ ONLY) ===")
        Try
            Dim part As Part = session.Parts.Work
            If part Is Nothing OrElse part.PartUnits <> BasePart.Units.Millimeters Then
                Throw New InvalidOperationException("Open the millimeter work part used for the failed batch.")
            End If
            Dim body As Body = Nothing
            For Each candidate As Body In part.Bodies
                If candidate.JournalIdentifier = SourceIdentifier AndAlso Not candidate.IsOccurrence Then
                    If body IsNot Nothing Then Throw New InvalidOperationException("Source identifier is ambiguous.")
                    body = candidate
                End If
            Next
            If body Is Nothing OrElse Not body.IsSheetBody Then Throw New InvalidOperationException("Source sheet not found: " & SourceIdentifier)
            Dim csys As CartesianCoordinateSystem = Nothing
            For Each feature As Feature In part.Features
                If TypeOf feature Is DatumCsys AndAlso feature.Timestamp = CsysTimestamp AndAlso Not feature.Suppressed Then
                    For Each entity As NXObject In feature.GetEntities()
                        If TypeOf entity Is CartesianCoordinateSystem Then
                            If csys IsNot Nothing Then Throw New InvalidOperationException("CSYS timestamp is ambiguous.")
                            csys = DirectCast(entity, CartesianCoordinateSystem)
                        End If
                    Next
                End If
            Next
            If csys Is Nothing Then Throw New InvalidOperationException("Datum CSYS timestamp 111 was not found. No substitute CSYS was chosen.")
            Dim origin As Point3d = csys.Origin
            Dim center As Double() = {origin.X, origin.Y, origin.Z}
            Dim loggedCenter As Double() = {2209.8614720507544, -644.23527382293537, 1246.3037184375648}
            If Distance(center, loggedCenter) > Tolerance Then
                Throw New InvalidOperationException("CSYS 111 no longer matches the reported center. Current center: " & Coordinates(center))
            End If
            Write("Source: " & body.JournalIdentifier)
            Write("CSYS timestamp: " & CsysTimestamp.ToString() & "; center: " & Coordinates(center) & "; radius: " & Number(Radius) & " mm")
            Write("Containment codes: 1 = on face interior, 2 = outside trimmed face, 3 = on face boundary.")
            Write("Reference coordinates are supplied directly; no new center point or sphere is created.")
            ' Latest failing face, earlier failing face, and a successful control face.
            For Each label As Integer In New Integer() {1595, 71, 66}
                Dim selected As Face = Nothing
                For Each face As Face In body.GetFaces()
                    If face.JournalIdentifier.StartsWith("FACE " & label.ToString(CultureInfo.InvariantCulture) & " ", StringComparison.Ordinal) Then
                        If selected IsNot Nothing Then Throw New InvalidOperationException("Face label is ambiguous: " & label.ToString())
                        selected = face
                    End If
                Next
                If selected Is Nothing Then
                    Write("FACE " & label.ToString() & " not found. No substitute face was inspected.")
                    Continue For
                End If
                InspectFace(selected, center)
            Next
            Write("=== END DIAGNOSTIC: model unchanged ===")
        Catch ex As Exception
            Write(ex.ToString())
            Write("=== DIAGNOSTIC STOPPED: no model changes made ===")
        End Try
    End Sub

    Private Sub InspectFace(face As Face, center As Double())
        Write("")
        Write("FACE: " & face.JournalIdentifier & "; current tag " & face.Tag.ToString())
        Dim generic As Measurement = Nothing
        Dim bounded As Measurement = Nothing
        Try
            generic = MeasureGeneric(face, center)
            PrintMeasurement("AskMinimumDist3 (level 2)", generic)
        Catch ex As Exception
            Write("Generic distance query failed: " & ex.ToString())
        End Try
        Try
            bounded = MeasureBounded(face, center)
            PrintMeasurement("AskMinimumFaceDist (within face boundaries)", bounded)
        Catch ex As Exception
            Write("Bounded-face query failed: " & ex.ToString())
        End Try

        If generic Is Nothing OrElse bounded Is Nothing Then
            Write("RESULT: incomplete measurements; no geometry classification is justified.")
        ElseIf bounded.Containment <> 1 AndAlso bounded.Containment <> 3 Then
            Write("RESULT: the bounded evaluator's point did not pass face containment. Investigate face validity/API behavior; do not skip it.")
        ElseIf generic.Containment = 2 AndAlso bounded.WitnessDistance > Radius + Tolerance Then
            Write("RESULT: generic witness is outside the face trim; bounded-face minimum is outside the sphere. Evidence of a false proximity candidate.")
        ElseIf bounded.WitnessDistance < Radius - Tolerance Then
            Write("RESULT: a checked point on the trimmed face lies inside the sphere. Do not discard this face as outside.")
            Write("The failed Intersect/Split cannot be explained by a wholly outside face; this report does not establish why they failed.")
        ElseIf generic.Containment <> 2 AndAlso generic.WitnessDistance < Radius - Tolerance Then
            Write("RESULT: queries disagree although the generic witness lies on the face. Investigate geometry; do not skip it.")
        Else
            Write("RESULT: no confirmed interior witness; compare both distances and the boundary tolerance before changing crop behavior.")
        End If
    End Sub

    Private Function MeasureGeneric(face As Face, center As Double()) As Measurement
        Dim answer As New Measurement()
        Dim first(2), second(2), guess(2) As Double
        ' NULL_TAG explicitly represents the supplied coordinate, as documented by UF.
        UFSession.GetUFSession().Modl.AskMinimumDist3(2, Tag.Null, face.Tag, 1, center, 0, guess,
            answer.Distance, first, second, answer.Accuracy)
        ValidatePoint(first)
        If Distance(first, center) > Tolerance Then Throw New InvalidOperationException("Generic query did not return the supplied reference point.")
        If Not Finite(answer.Distance) OrElse answer.Distance < 0 OrElse Not Finite(answer.Accuracy) OrElse answer.Accuracy < 0 Then
            Throw New InvalidOperationException("Generic query returned an invalid distance/accuracy.")
        End If
        CompleteMeasurement(answer, face, center, second)
        If Math.Abs(answer.Distance - answer.WitnessDistance) > Tolerance + answer.Accuracy Then
            Throw New InvalidOperationException("Generic distance disagrees with its returned witness coordinates.")
        End If
        Return answer
    End Function

    Private Function MeasureBounded(face As Face, center As Double()) As Measurement
        Dim uf As UFSession = UFSession.GetUFSession()
        Dim evaluator As IntPtr = IntPtr.Zero
        Try
            uf.Evalsf.Initialize(face.Tag, evaluator)
            Dim position As New UFEvalsf.Pos3()
            uf.Evalsf.AskMinimumFaceDist(evaluator, center, position)
            Dim answer As New Measurement()
            answer.Distance = position.distance
            ' This evaluator does not report an accuracy bound; do not claim an exact zero.
            answer.Accuracy = Double.NaN
            CompleteMeasurement(answer, face, center, position.pnt3)
            If Not Finite(answer.Distance) OrElse answer.Distance < 0 OrElse Math.Abs(answer.Distance - answer.WitnessDistance) > Tolerance Then
                Throw New InvalidOperationException("Bounded distance disagrees with its returned witness coordinates. Reported=" & Number(answer.Distance) & "; computed=" & Number(answer.WitnessDistance))
            End If
            Return answer
        Finally
            If evaluator <> IntPtr.Zero Then uf.Evalsf.Free(evaluator)
        End Try
    End Function

    Private Sub CompleteMeasurement(answer As Measurement, face As Face, center As Double(), point As Double())
        ValidatePoint(point)
        answer.Point = DirectCast(point.Clone(), Double())
        answer.WitnessDistance = Distance(center, point)
        UFSession.GetUFSession().Modl.AskPointContainment(point, face.Tag, answer.Containment)
    End Sub

    Private Sub PrintMeasurement(label As String, answer As Measurement)
        Write(label & ": distance=" & Number(answer.Distance) & " mm; witness distance=" & Number(answer.WitnessDistance) & " mm")
        Write("  closest point=" & Coordinates(answer.Point) & "; face containment=" & answer.Containment.ToString())
        Write("  reported accuracy=" & If(Double.IsNaN(answer.Accuracy), "not provided by this API", Number(answer.Accuracy) & " mm"))
    End Sub

    Private Sub ValidatePoint(point As Double())
        If point Is Nothing OrElse point.Length <> 3 Then Throw New InvalidOperationException("NX returned no valid witness point.")
        For Each value As Double In point
            If Not Finite(value) Then Throw New InvalidOperationException("NX returned a non-finite witness coordinate.")
        Next
    End Sub

    Private Function Finite(value As Double) As Boolean
        Return Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value)
    End Function

    Private Function Distance(first As Double(), second As Double()) As Double
        Return Math.Sqrt((first(0) - second(0)) ^ 2 + (first(1) - second(1)) ^ 2 + (first(2) - second(2)) ^ 2)
    End Function

    Private Function Coordinates(point As Double()) As String
        Return "(" & Number(point(0)) & ", " & Number(point(1)) & ", " & Number(point(2)) & ")"
    End Function

    Private Function Number(value As Double) As String
        Return value.ToString("R", CultureInfo.InvariantCulture)
    End Function

    Private Sub Write(message As String)
        NXOpen.Session.GetSession().ListingWindow.WriteLine(message)
    End Sub
End Module
