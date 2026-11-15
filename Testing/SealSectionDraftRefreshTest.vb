Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.Drawings
Imports NXOpen.UF

' Repair only generated disk-sector views on the current drawing sheet.
' Reuses existing curves; no new sections, sheets or views are generated.
Module SealSectionDraftRefreshTest
    Private Const Title As String = "Refresh Seal Section Views / 断面ビューを更新"

    Private Class ViewJob
        Public View As BaseView
        Public Csys As CartesianCoordinateSystem
        Public Layers As New List(Of Integer)()
        Public Curves As New List(Of Curve)()
        Public Inherited As Boolean
        Public CurveLayers As New Dictionary(Of Tag, Integer)()
        Public OriginOnSheet As Double()
    End Class

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim mark As Session.UndoMarkId
        Dim changed As Boolean = False
        Dim stage As String = "checking the current drawing sheet"
        session.ListingWindow.Open()
        session.ListingWindow.WriteLine("=== SEAL SECTION DRAFT REFRESH v2.2 (VIEW ERASURE) ===")
        Try
            Dim part As Part = session.Parts.Work
            If part Is Nothing OrElse session.Parts.Display Is Nothing OrElse session.Parts.Display.Tag <> part.Tag Then
                Throw New InvalidOperationException("Display the work part containing the generated drawing." & Environment.NewLine & "作成した図面を含む作業パートを表示してください。")
            End If
            Dim sheet As DrawingSheet = part.DrawingSheets.CurrentDrawingSheet
            If sheet Is Nothing Then Throw New InvalidOperationException("Open the generated drawing sheet in Drafting, then run this journal." & Environment.NewLine & "ドラフティングで作成済みの図面シートを開いてから実行してください。")
            Dim datums As New Dictionary(Of Integer, CartesianCoordinateSystem)()
            Dim sectorLayers As New List(Of Integer)()
            Dim inheritedFeatures As New Dictionary(Of String, List(Of Feature))()
            For Each feature As Feature In part.Features
                If feature.Suppressed Then Continue For
                If TypeOf feature Is DatumCsys Then
                    For Each entity As NXObject In feature.GetEntities()
                        If TypeOf entity Is CartesianCoordinateSystem Then datums.Add(feature.Timestamp, DirectCast(entity, CartesianCoordinateSystem))
                    Next
                ElseIf TypeOf feature Is IntersectionCurve AndAlso
                    (feature.HasUserAttribute("SEAL_BATCH_ID", NXObject.AttributeType.String, -1) OrElse
                    feature.Name.StartsWith("SEAL_DISK_SECTOR_", StringComparison.Ordinal)) Then
                    If feature.HasUserAttribute("SEAL_BATCH_ID", NXObject.AttributeType.String, -1) Then
                        Dim key As String = feature.GetStringUserAttribute("SEAL_BATCH_ID", -1) & ":" &
                            feature.GetIntegerUserAttribute("SEAL_SECTOR_TIMESTAMP", -1).ToString(CultureInfo.InvariantCulture)
                        If Not inheritedFeatures.ContainsKey(key) Then inheritedFeatures.Add(key, New List(Of Feature)())
                        inheritedFeatures(key).Add(feature)
                        Continue For
                    End If
                    Dim layer As Integer = 0
                    For Each entity As NXObject In feature.GetEntities()
                        If Not TypeOf entity Is Curve Then Continue For
                        Dim curve As Curve = DirectCast(entity, Curve)
                        If layer <> 0 AndAlso curve.Layer <> layer Then Throw New InvalidOperationException("Sector curves occupy multiple layers: " & feature.Name)
                        layer = curve.Layer
                    Next
                    If layer <> 0 AndAlso Not sectorLayers.Contains(layer) Then sectorLayers.Add(layer)
                End If
            Next
            Dim jobs As New List(Of ViewJob)()
            For Each drawingView As DraftingView In sheet.GetDraftingViews()
                Dim view As BaseView = TryCast(drawingView, BaseView)
                If view Is Nothing Then Continue For
                Dim timestamp As Integer
                Dim inherited As Boolean = view.HasUserAttribute("SEAL_LAYER_MODE", NXObject.AttributeType.String, -1)
                If inherited Then
                    ' SOURCE views may have collision suffixes or be manually renamed.
                    ' The stored timestamp and batch ID identify them, not display names.
                    If view.GetStringUserAttribute("SEAL_LAYER_MODE", -1) <> "SOURCE" Then
                        Throw New InvalidOperationException("Invalid sector metadata on " & view.Name)
                    End If
                    timestamp = view.GetIntegerUserAttribute("SEAL_SECTOR_TIMESTAMP", -1)
                    If Not datums.ContainsKey(timestamp) Then Throw New InvalidOperationException("No matching Datum CSYS for " & view.Name)
                Else
                    ' Legacy journals did not store sector metadata. Keep their exact
                    ' SECTOR_<timestamp> lookup; never guess from a suffixed name.
                    If Not view.Name.StartsWith("SECTOR_", StringComparison.Ordinal) Then Continue For
                    If Not Integer.TryParse(view.Name.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, timestamp) OrElse Not datums.ContainsKey(timestamp) Then Continue For
                End If
                Dim job As New ViewJob()
                job.View = view
                job.Csys = datums(timestamp)
                If inherited Then
                    Dim key As String = view.GetStringUserAttribute("SEAL_BATCH_ID", -1) & ":" & timestamp.ToString(CultureInfo.InvariantCulture)
                    If Not inheritedFeatures.ContainsKey(key) Then Throw New InvalidOperationException("No matching source-layer features for " & view.Name)
                    job.Inherited = True
                    For Each feature As Feature In inheritedFeatures(key)
                        Dim sourceLayer As Integer = feature.GetIntegerUserAttribute("SEAL_SOURCE_LAYER", -1)
                        If sourceLayer < 1 OrElse sourceLayer > 256 Then Throw New InvalidOperationException("Invalid source layer on " & feature.Name)
                        For Each entity As NXObject In feature.GetEntities()
                            Dim curve As Curve = TryCast(entity, Curve)
                            If curve Is Nothing Then Continue For
                            job.Curves.Add(curve)
                            job.CurveLayers.Add(curve.Tag, sourceLayer)
                            If Not job.Layers.Contains(sourceLayer) Then job.Layers.Add(sourceLayer)
                        Next
                    Next
                    If job.Curves.Count = 0 Then Throw New InvalidOperationException("No current output curves for " & view.Name)
                    job.OriginOnSheet = MapOrigin(view, job.Csys)
                    jobs.Add(job)
                    Continue For
                End If
                Dim states As NXOpen.Layer.StateInfo() = Nothing
                part.Layers.GetVisibilitiesInView(view, states)
                Dim layer As Integer = 0
                Dim visibleCount As Integer = 0
                For Each state As NXOpen.Layer.StateInfo In states
                    If state.State <> NXOpen.Layer.State.Visible Then Continue For
                    visibleCount += 1
                    layer = state.Layer
                Next
                If visibleCount <> 1 OrElse Not sectorLayers.Contains(layer) Then
                    Throw New InvalidOperationException(view.Name & " does not have one identifiable disk-sector layer. No changes made; inspect its layer visibility first.")
                End If
                job.Layers.Add(layer)
                job.OriginOnSheet = MapOrigin(view, job.Csys)
                jobs.Add(job)
            Next
            If jobs.Count = 0 Then Throw New InvalidOperationException("No matching generated disk-sector base views found on the current sheet.")
            mark = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, Title)
            changed = True
            Dim updates As New List(Of DraftingView)()
            For Each job As ViewJob In jobs
                stage = "refreshing " & job.View.Name
                If job.Inherited Then
                    ' New output entities after a geometry update may start on the work
                    ' layer. Reapply the category recorded when the batch was generated.
                    For Each curve As Curve In job.Curves
                        curve.Layer = job.CurveLayers(curve.Tag)
                        curve.RedisplayObject()
                    Next
                End If
            Next
            For Each job As ViewJob In jobs
                stage = "refreshing " & job.View.Name
                Dim builder As BaseViewBuilder = part.DraftingViews.CreateBaseViewBuilder(job.View)
                Try
                    ' Display all selected model curves without body-driven hidden-line removal.
                    builder.Style.ViewStyleHiddenLines.IncludeModelCurvesOption = NXOpen.Preferences.IncludeModelCurvesOption.No
                    If job.Inherited Then builder.HiddenObjects.Objects.Clear()
                    For Each body As Body In part.Bodies
                        If Not builder.HiddenObjects.Objects.Contains(body) AndAlso Not builder.HiddenObjects.Objects.Add(body) Then
                            Throw New InvalidOperationException("NX rejected a view-specific hidden body.")
                        End If
                    Next
                    ' The hidden-object builder handles bodies. Use DependentDisplay
                    ' below for individual curves sharing a layer with other sectors.
                    If Not builder.Validate() Then Throw New InvalidOperationException("NX rejected the revised view settings.")
                    If builder.Commit() Is Nothing Then Throw New InvalidOperationException("NX returned no edited view.")
                Finally
                    builder.Destroy()
                End Try
                Dim states(255) As NXOpen.Layer.StateInfo
                For number As Integer = 1 To 256
                    states(number - 1) = New NXOpen.Layer.StateInfo(number,
                        If(job.Layers.Contains(number), NXOpen.Layer.State.Visible, NXOpen.Layer.State.Hidden))
                Next
                part.Layers.SetObjectsVisibilityOnLayer(job.View, states, False)
                If job.Inherited Then
                    IsolateSectorCurves(part, job.View, job.Curves)
                    job.View.SetUserAttribute("SEAL_VISIBILITY_MODE", -1, "VIEW_DEPENDENT_ERASE", Update.Option.Now)
                End If
                job.View.Update()
                ' Keep the existing CSYS position in the grid when the view bounds shrink.
                Dim actual As Double() = MapOrigin(job.View, job.Csys)
                Dim reference As Point3d = job.View.GetDrawingReferencePoint()
                job.View.MoveView(New Point3d(reference.X + job.OriginOnSheet(0) - actual(0),
                    reference.Y + job.OriginOnSheet(1) - actual(1), 0))
                job.View.Update()
                updates.Add(job.View)
                session.ListingWindow.WriteLine(job.View.Name & ": curve-only settings applied, bodies hidden within this view, view regenerated.")
            Next
            stage = "updating the repaired drawing views"
            If session.UpdateManager.DoUpdate(mark) <> 0 Then Throw New InvalidOperationException("NX reported update errors.")
            part.DraftingViews.UpdateViews(updates.ToArray())
            For Each job As ViewJob In jobs
                If job.Inherited Then VerifySectorCurves(job.View, job.Curves)
            Next
            changed = False
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Refreshed " & jobs.Count.ToString() & " sector views. Model-body visibility is unchanged. Check the complete curve profiles in Drafting." & Environment.NewLine &
                jobs.Count.ToString() & "個の断面ビューを更新しました。ボディの表示状態は変更していません。図面で断面曲線を確認してください。")
        Catch ex As Exception
            Dim cleanup As String = ""
            If changed Then
                Try
                    session.UndoToMark(mark, Nothing)
                    cleanup = Environment.NewLine & "View changes rolled back. Existing section geometry retained."
                Catch undoError As Exception
                    cleanup = Environment.NewLine & "Rollback error: " & undoError.Message
                End Try
            End If
            session.ListingWindow.WriteLine(stage & Environment.NewLine & ex.ToString() & cleanup)
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, stage & Environment.NewLine & ex.Message & cleanup)
        End Try
    End Sub

    Private Sub IsolateSectorCurves(part As Part, view As NXOpen.Drawings.DraftingView, curves As List(Of Curve))
        If curves.Count = 0 Then Throw New InvalidOperationException("Cannot isolate an empty sector.")
        Dim keep As New Dictionary(Of Tag, Boolean)()
        Dim ownObjects As New List(Of DisplayableObject)()
        Dim layers As New List(Of Integer)()
        For Each curve As Curve In curves
            keep.Add(curve.Tag, True)
            ownObjects.Add(curve)
            If Not layers.Contains(curve.Layer) Then layers.Add(curve.Layer)
        Next
        Dim excluded As New List(Of DisplayableObject)()
        For Each number As Integer In layers
            For Each item As NXObject In part.Layers.GetAllObjectsOnLayer(number)
                Dim displayable As DisplayableObject = TryCast(item, DisplayableObject)
                If displayable Is Nothing OrElse TypeOf displayable Is Body OrElse keep.ContainsKey(displayable.Tag) Then Continue For
                excluded.Add(displayable)
            Next
        Next
        ' Erase is a persistent display edit in THIS view, not a geometry deletion
        ' or a global Blank(). Keep the inherited numeric layers on the curves.
        view.DependentDisplay.RemoveErasureOnObjectAndSubobjects(ownObjects.ToArray(), False)
        If excluded.Count > 0 Then view.DependentDisplay.Erase(excluded.ToArray())
        NXOpen.Session.GetSession().ListingWindow.WriteLine("View isolation: " & ownObjects.Count.ToString() &
            " sector curves; " & excluded.Count.ToString() & " other objects erased in this view.")
    End Sub

    Private Sub VerifySectorCurves(view As NXOpen.Drawings.DraftingView, curves As List(Of Curve))
        Dim keep As New Dictionary(Of Tag, Boolean)()
        For Each curve As Curve In curves
            keep(curve.Tag) = True
        Next
        Dim matched As Integer = 0
        For Each item As DisplayableObject In view.AskVisibleObjects()
            ' Check model-curve identities returned by NX. Drafting proxies and
            ' clipped/hidden curves are not a complete geometric-profile audit.
            If Not TypeOf item Is Curve Then Continue For
            If Not keep.ContainsKey(item.Tag) Then Throw New InvalidOperationException(
                view.Name & ": unrelated model curve remains visible (tag " & item.Tag.ToString() &
                "). / 別の断面の曲線が表示されています。ビューの分離に失敗しました。")
            matched += 1
        Next
        NXOpen.Session.GetSession().ListingWindow.WriteLine(view.Name & ": visibility query returned " & matched.ToString() &
            " own model curves and no unrelated model curves. Check full profiles visually.")
    End Sub

    Private Function MapOrigin(view As BaseView, csys As CartesianCoordinateSystem) As Double()
        Dim origin As Point3d = csys.Origin
        Dim drawingPoint(1) As Double
        UFSession.GetUFSession().View.MapModelToDrawing(view.Tag, New Double() {origin.X, origin.Y, origin.Z}, drawingPoint)
        Return drawingPoint
    End Function
End Module
