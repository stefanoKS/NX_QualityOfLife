Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports NXOpen
Imports NXOpen.Features
Imports NXOpen.UF
Imports System.Diagnostics
Imports System.IO
Imports System.Text

' NX 2406 test: associative bounded square/circle intersection; no body cropping.
Module SealSectionDiskBatchTest
    Private Const Title As String = "Disk Section Batch Test / 断面一括作成テスト"
    Private Const Tolerance As Double = 0.01
    Private timing As SealTimingLog
    Private perfContext As String = ""
    Private Enum WindowShape
        Square
        Circle
    End Enum

    Public Sub Main()
        Dim session As Session = NXOpen.Session.GetSession()
        Dim part As Part = session.Parts.Work
        Dim mark As Session.UndoMarkId
        Dim changed As Boolean = False
        Dim stage As String = "checking inputs"
        Dim outcome As String = "CANCELLED"
        Try
            timing = New SealTimingLog()
            perfContext = ""
            session.ListingWindow.Open()
            Report(timing.Notice)
            If part Is Nothing OrElse part.PartUnits <> BasePart.Units.Millimeters Then
                Throw New InvalidOperationException("Open a millimeter part in Modeling. / モデリングでミリメートル単位のパートを開いてください。")
            End If
            If session.Parts.Display Is Nothing OrElse session.Parts.Display.Tag <> part.Tag Then Throw New InvalidOperationException("Display the work part before running this journal. / 作業パートを表示してから実行してください。")
            Dim surfaces As FeatureGroup = ChooseGroup(part, "Step 1 - Source body feature group (sheets / solids)" & Environment.NewLine &
                "手順1：対象ボディ（シート／ソリッド）のフィーチャグループを選択")
            If surfaces Is Nothing Then Return
            Dim sectors As FeatureGroup = ChooseGroup(part, "Step 2 - CSYS feature group" & Environment.NewLine &
                "手順2：データム座標系（CSYS）のフィーチャグループを選択")
            If sectors Is Nothing Then Return
            timing.Info("Source group=" & surfaces.Name & "; CSYS group=" & sectors.Name)
            Report("Selected group timestamps: source=" & surfaces.Timestamp.ToString() & "; CSYS=" & sectors.Timestamp.ToString())
            Dim bodyList As New List(Of Body)()
            Dim bodyTags As New List(Of Tag)()
            Dim sourceReport As New List(Of String)()
            Using phaseTiming As IDisposable = Perf("Sources.CollectBodies / 入力ボディ収集")
                For Each feature As Feature In Members(surfaces)
                    Dim featureBodies As Body() = feature.GetBodies()
                    Dim added As Integer = 0
                    For Each body As Body In featureBodies
                        If (body.IsSheetBody OrElse body.IsSolidBody) AndAlso Not body.IsOccurrence AndAlso Not bodyTags.Contains(body.Tag) Then
                            bodyTags.Add(body.Tag)
                            bodyList.Add(body)
                            added += 1
                        End If
                    Next
                    If featureBodies.Length > 0 Then sourceReport.Add(feature.JournalIdentifier & ": " & featureBodies.Length.ToString() &
                        " result bodies; " & added.ToString() & " unique sheet/solid bodies added.")
                Next
            End Using
            Dim datums As New List(Of DatumCsys)()
            For Each feature As Feature In Members(sectors)
                If TypeOf feature Is DatumCsys Then datums.Add(DirectCast(feature, DatumCsys))
            Next
            datums.Sort(Function(left, right) left.Timestamp.CompareTo(right.Timestamp))
            If bodyList.Count = 0 OrElse datums.Count = 0 Then Throw New InvalidOperationException("The groups must contain sheet/solid bodies and Datum CSYS features respectively." & Environment.NewLine & "手順1ではシート／ソリッドボディ、手順2ではデータム座標系を含むグループを選択してください。")
            Dim shape As WindowShape
            If Not ChooseShape(shape) Then Return
            Dim size As Double
            Dim width As Double
            Dim height As Double
            Dim gap As Double
            If shape = WindowShape.Square Then
                If Not ReadNumber("Step 4 - Square full side length in mm (not half-width)" & Environment.NewLine &
                    "手順4：正方形の一辺の長さ（mm、半幅ではありません）", "50", False, size) Then Return
            Else
                If Not ReadNumber("Step 4 - Circle radius in mm (not diameter)" & Environment.NewLine &
                    "手順4：円の半径（mm、直径ではありません）", "25", False, size) Then Return
            End If
            If Not ReadNumber("Step 5 - Drawing width in mm" & Environment.NewLine & "手順5：図面の幅（mm）", "5000", False, width) Then Return
            If Not ReadNumber("Step 6 - Drawing height in mm" & Environment.NewLine & "手順6：図面の高さ（mm）", "5000", False, height) Then Return
            If Not ReadNumber("Step 7 - Sector gap / sheet margin in mm" & Environment.NewLine & "手順7：断面間の間隔・図面端の余白（mm）", "10", True, gap) Then Return
            Dim halfSize As Double = If(shape = WindowShape.Square, size / 2.0, size)
            Dim span As Double = 2 * halfSize
            Dim pitch As Double = span + gap
            If Double.IsInfinity(pitch) OrElse halfSize <= Tolerance Then Throw New InvalidOperationException(
                "Cutting size is too large or too small for 0.01 mm tolerance. / 切断範囲が大きすぎるか、0.01 mmの許容差に対して小さすぎます。")
            Dim columnsFit As Double = Math.Floor((width - gap) / pitch)
            Dim rowsFit As Double = Math.Floor((height - gap) / pitch)
            If columnsFit < 1 OrElse rowsFit < 1 OrElse columnsFit * rowsFit < datums.Count Then
                Throw New InvalidOperationException("Drawing is too small for " & datums.Count.ToString() &
                    " sectors at 1:1. Capacity is " & Math.Max(0, columnsFit).ToString() & " columns x " &
                    Math.Max(0, rowsFit).ToString() & " rows. Each cell needs " &
                    span.ToString() & " mm (square side / circle diameter) plus the gap. Increase drawing size or reduce cutting size/gap." &
                    Environment.NewLine & "図面サイズが不足しています。各断面には正方形の一辺または円の直径＋間隔が必要です。図面を大きくするか、切断範囲・間隔を小さくしてください。")
            End If
            Dim columns As Integer = CInt(Math.Min(columnsFit, datums.Count))
            Dim rows As Integer = CInt(Math.Ceiling(CDbl(datums.Count) / columns))
            session.ListingWindow.Open()
            Report("=== DISK SECTION BATCH TEST v5.1.2 (DRAFTING PROXY FILTER / TIMING LOG) ===")
            Report("Boundary: " & shape.ToString() & "; " &
                If(shape = WindowShape.Square, "full side / 一辺 = ", "radius / 半径 = ") & size.ToString("R", CultureInfo.InvariantCulture) & " mm; center = CSYS origin, plane = CSYS XY.")
            timing.Info("Drawing width=" & width.ToString("R", CultureInfo.InvariantCulture) & "; height=" & height.ToString("R", CultureInfo.InvariantCulture) &
                "; gap=" & gap.ToString("R", CultureInfo.InvariantCulture) & "; units=mm; tolerance=" & Tolerance.ToString("R", CultureInfo.InvariantCulture))
            For Each line As String In sourceReport
                Report(line)
            Next
            Dim sheetCount As Integer = 0
            Dim solidCount As Integer = 0
            For Each body As Body In bodyList
                If body.IsSolidBody Then
                    solidCount += 1
                Else
                    sheetCount += 1
                End If
            Next
            Report("Sources: " & sheetCount.ToString() & " sheet bodies, " & solidCount.ToString() & " solid bodies.")
            Report("Grid " & columns.ToString() & " x " & rows.ToString() & "; ascending CSYS timestamp, left to right, top to bottom.")
            ' A feature may return bodies on different layers. Partition the actual bodies,
            ' never infer a curve's owner from proximity or the current work layer.
            Dim sourceLayers As New SortedDictionary(Of Integer, List(Of Body))()
            For Each body As Body In bodyList
                If Not sourceLayers.ContainsKey(body.Layer) Then sourceLayers.Add(body.Layer, New List(Of Body)())
                sourceLayers(body.Layer).Add(body)
            Next
            For Each bucket As KeyValuePair(Of Integer, List(Of Body)) In sourceLayers
                Report("Source layer " & bucket.Key.ToString() & ": " & bucket.Value.Count.ToString() & " bodies.")
            Next
            If sourceLayers.Count = 1 Then Report(
                "Single source layer: supported; views isolate sectors by object. / 全ボディが同一レイヤでも使用できます。各ビューで断面ごとに表示を分離します。")
            Dim batchId As String = Guid.NewGuid().ToString("N")
            timing.Info("Batch ID=" & batchId)
            Dim sectorCurves As New Dictionary(Of Integer, List(Of Curve))()
            mark = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, Title)
            changed = True
            Dim sizeExpression As Expression = part.Expressions.CreateSystemExpressionWithUnits(
                size.ToString("R", CultureInfo.InvariantCulture), DirectCast(part.UnitCollection.FindObject("MilliMeter"), Unit))
            Report(If(shape = WindowShape.Square, "Square side expression: ", "Circle radius expression: ") & sizeExpression.Name)
            Dim completed As New List(Of Integer)()
            For index As Integer = 0 To datums.Count - 1
                Dim datum As DatumCsys = datums(index)
                stage = "sector " & (index + 1).ToString() & " / " & datum.Name
                perfContext = stage & "; timestamp=" & datum.Timestamp.ToString()
                Using sectorTiming As IDisposable = Perf("Sector.Total / 断面全体")
                    Dim csys As CartesianCoordinateSystem = GetCsys(datum)
                    stage = "creating " & shape.ToString() & " cutting sheet for sector " & (index + 1).ToString()
                    Dim disk As Body
                    If shape = WindowShape.Square Then
                        disk = MakeSquare(part, csys, sizeExpression, size)
                    Else
                        disk = MakeDisk(part, csys, sizeExpression, size)
                    End If
                    If index = 0 Then
                        stage = "checking bounded " & shape.ToString() & " intersection"
                        CheckWindowIntersection(part, disk, csys, halfSize, shape)
                    End If
                    Dim curves As New List(Of Curve)()
                    For Each bucket As KeyValuePair(Of Integer, List(Of Body)) In sourceLayers
                        stage = "intersecting sector " & (index + 1).ToString() & ", source layer " & bucket.Key.ToString()
                        perfContext = stage & "; bodies=" & bucket.Value.Count.ToString()
                        Dim section As Feature = IntersectLayer(part, bucket.Value, disk, stage)
                        If section Is Nothing Then Continue For
                        Dim layerCurves As New List(Of Curve)()
                        For Each entity As NXObject In section.GetEntities()
                            If TypeOf entity Is Curve Then layerCurves.Add(DirectCast(entity, Curve))
                        Next
                        CheckCurveSamples(layerCurves, csys, halfSize, shape)
                        Using assignmentTiming As IDisposable = Perf("Curves.LayersAndMetadata / レイヤ・属性設定", "curves=" & layerCurves.Count.ToString())
                            For Each curve As Curve In layerCurves
                                curve.Layer = bucket.Key
                                curve.RedisplayObject()
                            Next
                            section.SetName("SEAL_DISK_SECTOR_" & (index + 1).ToString("000") & "_L" & bucket.Key.ToString("000"))
                            section.SetUserAttribute("SEAL_BATCH_ID", -1, batchId, Update.Option.Now)
                            section.SetUserAttribute("SEAL_SECTOR_TIMESTAMP", -1, datum.Timestamp, Update.Option.Now)
                            section.SetUserAttribute("SEAL_SOURCE_LAYER", -1, bucket.Key, Update.Option.Now)
                            section.SetUserAttribute("SEAL_BOUNDARY_SHAPE", -1, shape.ToString(), Update.Option.Now)
                        End Using
                        curves.AddRange(layerCurves)
                        Report("Sector " & (index + 1).ToString() & ", source layer " & bucket.Key.ToString() &
                            ": " & layerCurves.Count.ToString() & " curves on inherited layer.")
                    Next
                    If curves.Count = 0 Then
                        Report("Sector " & (index + 1).ToString() & ": no intersection curves; its grid cell remains empty.")
                        Continue For
                    End If
                    sectorCurves.Add(index, curves)
                    completed.Add(index)
                    Report("Sector " & (index + 1).ToString() & ": " & datum.Name &
                        ", timestamp " & datum.Timestamp.ToString() & ", " & curves.Count.ToString() & " curves.")
                End Using
            Next
            perfContext = "batch"
            If completed.Count = 0 Then Throw New InvalidOperationException("No sectors produced intersection curves.")
            stage = "updating section geometry before drafting"
            If UpdatePart(session, mark) <> 0 Then Throw New InvalidOperationException("NX reported section geometry update errors.")
            stage = "creating the drawing sheet"
            CreateSheet(part, width, height)
            Dim createdViews As New List(Of NXOpen.Drawings.DraftingView)()
            For Each index As Integer In completed
                stage = "placing drawing sector " & (index + 1).ToString()
                perfContext = stage & "; timestamp=" & datums(index).Timestamp.ToString()
                Dim column As Integer = index Mod columns
                Dim row As Integer = index \ columns
                Dim position As New Point3d(gap + halfSize + column * pitch, height - gap - halfSize - row * pitch, 0)
                createdViews.Add(CreateDrawing(part, datums(index), position, sectorCurves(index), batchId))
            Next
            perfContext = "batch"
            If UpdatePart(session, mark) <> 0 Then Throw New InvalidOperationException("NX reported update errors.")
            stage = "regenerating curve-only drawing views"
            Using updateTiming As IDisposable = Perf("Views.FinalUpdate / 全ビュー最終更新", "views=" & createdViews.Count.ToString())
                part.DraftingViews.UpdateViews(createdViews.ToArray())
            End Using
            Dim viewIndex As Integer = 0
            For Each index As Integer In completed
                perfContext = "verifying drawing sector " & (index + 1).ToString()
                VerifySectorCurves(createdViews(viewIndex), sectorCurves(index))
                viewIndex += 1
            Next
            Dim remainingBodies As New List(Of Tag)()
            For Each existing As Body In part.Bodies
                remainingBodies.Add(existing.Tag)
            Next
            For Each original As Body In bodyList
                If Not remainingBodies.Contains(original.Tag) Then
                    Throw New InvalidOperationException("An original source body is missing after the batch.")
                End If
            Next
            changed = False
            outcome = "SUCCESS"
            FinishTiming(outcome)
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Information,
                "Created " & completed.Count.ToString() & " sector views. See the Listing Window for the ordered sector list." & Environment.NewLine &
                completed.Count.ToString() & "個の断面ビューを作成しました。順序は情報ウィンドウで確認してください。" & Environment.NewLine & timing.Notice)
        Catch ex As Exception
            outcome = "FAILED"
            If timing IsNot Nothing Then timing.Info("ERROR " & stage & Environment.NewLine & ex.ToString())
            Dim cleanup As String = ""
            If changed Then
                Try
                    Using undoTiming As IDisposable = Perf("Batch.Rollback / 一括処理の取消", stage)
                        session.UndoToMark(mark, Nothing)
                    End Using
                    cleanup = Environment.NewLine & "Batch changes rolled back."
                Catch undoError As Exception
                    cleanup = Environment.NewLine & "Rollback error: " & undoError.Message
                End Try
            End If
            session.ListingWindow.Open()
            Dim nxError As NXException = TryCast(ex, NXException)
            If nxError IsNot Nothing Then Report("NX error code: " & nxError.ErrorCode.ToString())
            Report(stage & Environment.NewLine & ex.ToString() & cleanup)
            FinishTiming(outcome)
            NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Error, stage & Environment.NewLine & ex.Message & cleanup &
                If(timing Is Nothing, "", Environment.NewLine & timing.Notice))
        Finally
            FinishTiming(outcome)
        End Try
    End Sub

    Private Function Perf(name As String, Optional details As String = "") As IDisposable
        Return timing.BeginStep(name, perfContext & If(details.Length = 0, "", "; " & details))
    End Function

    Private Sub Report(message As String)
        If timing IsNot Nothing Then timing.Info(message)
        NXOpen.Session.GetSession().ListingWindow.WriteLine(message)
    End Sub

    Private Sub FinishTiming(outcome As String)
        If timing Is Nothing OrElse timing.Finished Then Return
        timing.Finish(outcome)
        Report(timing.Summary)
        Report(timing.Notice)
    End Sub

    Private Function UpdatePart(session As Session, mark As Session.UndoMarkId) As Integer
        Using updateTiming As IDisposable = Perf("Model.Update / モデル更新")
            Return session.UpdateManager.DoUpdate(mark)
        End Using
    End Function

    Private Function Members(group As FeatureGroup) As List(Of Feature)
        Using functionTiming As IDisposable = Perf("Sources.GroupMembers / グループ展開")
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
        End Using
    End Function

    Private Function ChooseGroup(part As Part, prompt As String) As FeatureGroup
        Using functionTiming As IDisposable = Perf("Sources.GroupPicker / グループ一覧準備")
            Dim groups As New List(Of FeatureGroup)()
            For Each feature As Feature In part.Features
                If TypeOf feature Is FeatureGroup AndAlso Not feature.Suppressed Then groups.Add(DirectCast(feature, FeatureGroup))
            Next
            groups.Sort(Function(left, right) left.Timestamp.CompareTo(right.Timestamp))
            If groups.Count = 0 Then Throw New InvalidOperationException("No feature groups found. / フィーチャグループがありません。")
            Using dialog As New Form()
                dialog.Text = Title
                dialog.Font = New System.Drawing.Font("Yu Gothic UI", 10.0F)
                dialog.AutoScaleMode = AutoScaleMode.Font
                dialog.ClientSize = New System.Drawing.Size(720, 240)
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog
                dialog.MaximizeBox = False
                dialog.MinimizeBox = False
                dialog.StartPosition = FormStartPosition.CenterParent
                Dim label As New System.Windows.Forms.Label With {.Text = prompt, .Left = 15, .Top = 12, .Width = 690, .Height = 58}
                Dim choices As New ComboBox With {.Left = 15, .Top = 78, .Width = 690, .DropDownStyle = ComboBoxStyle.DropDownList}
                For Each group As FeatureGroup In groups
                    choices.Items.Add(group.Name & " [timestamp " & group.Timestamp.ToString() & "]")
                Next
                choices.SelectedIndex = 0
                Dim hint As New System.Windows.Forms.Label With {.Text =
                    "All source bodies may share one layer; separate layers are optional." & Environment.NewLine &
                    "全ボディが同一レイヤでも使用できます。レイヤ分けは任意です。", .Left = 15, .Top = 120, .Width = 690, .Height = 52}
                Dim ok As New Button With {.Text = "Select / 選択", .Left = 405, .Top = 188, .Width = 145, .Height = 36, .DialogResult = DialogResult.OK}
                Dim cancel As New Button With {.Text = "Cancel / キャンセル", .Left = 560, .Top = 188, .Width = 145, .Height = 36, .DialogResult = DialogResult.Cancel}
                dialog.Controls.AddRange(New Control() {label, choices, hint, ok, cancel})
                dialog.AcceptButton = ok
                dialog.CancelButton = cancel
                NXOpenUI.FormUtilities.ReparentForm(dialog)
                Using inputTiming As IDisposable = Perf("Input.Dialog / 入力待ち", prompt)
                    If dialog.ShowDialog() <> DialogResult.OK Then Return Nothing
                End Using
                Return groups(choices.SelectedIndex)
            End Using
        End Using
    End Function

    Private Function ChooseShape(ByRef shape As WindowShape) As Boolean
        Using dialog As New SealBoundaryShapePicker()
            NXOpenUI.FormUtilities.ReparentForm(dialog)
            Using inputTiming As IDisposable = Perf("Input.Dialog / 入力待ち", "shape / 形状")
                If dialog.ShowDialog() <> DialogResult.OK Then Return False
            End Using
            shape = If(dialog.IsSquare, WindowShape.Square, WindowShape.Circle)
            Return True
        End Using
    End Function

    Private Function ReadNumber(prompt As String, defaultText As String, allowZero As Boolean, ByRef value As Double) As Boolean
        Do
            Dim limits As String = If(allowZero,
                "Enter 0 or a positive number. / 0以上の数値を入力してください。",
                "Enter a positive number. / 0より大きい数値を入力してください。")
            Dim answer As String
            Using inputTiming As IDisposable = Perf("Input.Dialog / 入力待ち", prompt)
                answer = Interaction.InputBox(prompt & Environment.NewLine & limits & Environment.NewLine &
                    "Cancel or blank: stop. / キャンセルまたは空欄：処理を中止します。", Title, defaultText)
            End Using
            If String.IsNullOrWhiteSpace(answer) Then Return False
            If Double.TryParse(answer, NumberStyles.Float, CultureInfo.CurrentCulture, value) AndAlso
                Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value) AndAlso
                (value > 0 OrElse (allowZero AndAlso value = 0)) Then Return True
            Using inputTiming As IDisposable = Perf("Input.Dialog / 入力待ち", "invalid dimension / 寸法入力エラー")
                NXOpen.UI.GetUI().NXMessageBox.Show(Title, NXMessageBox.DialogType.Warning, "Invalid dimension. / 寸法の入力が正しくありません。" & Environment.NewLine & limits)
            End Using
        Loop
    End Function


    Private Function GetCsys(datum As DatumCsys) As CartesianCoordinateSystem
        Using functionTiming As IDisposable = Perf("CSYS.Resolve / 座標系取得")
            Dim result As CartesianCoordinateSystem = Nothing
            For Each entity As NXObject In datum.GetEntities()
                If TypeOf entity Is CartesianCoordinateSystem Then
                    If result IsNot Nothing Then Throw New InvalidOperationException("Multiple Cartesian systems in datum.")
                    result = DirectCast(entity, CartesianCoordinateSystem)
                End If
            Next
            If result Is Nothing Then Throw New InvalidOperationException("No Cartesian system in datum.")
            Return result
        End Using
    End Function

    Private Function MakeSquare(part As Part, csys As CartesianCoordinateSystem, widthExpression As Expression, width As Double) As Body
        Using functionTiming As IDisposable = Perf("Boundary.Square / 正方形作成")
            Dim update As SmartObject.UpdateOption = SmartObject.UpdateOption.WithinModeling
            Dim mm As Unit = DirectCast(part.UnitCollection.FindObject("MilliMeter"), Unit)
            Dim halfExpression As Expression = part.Expressions.CreateSystemExpressionWithUnits(widthExpression.Name & "/2", mm)
            Dim negativeExpression As Expression = part.Expressions.CreateSystemExpressionWithUnits("-" & halfExpression.Name, mm)
            Dim positive As Scalar = part.Scalars.CreateScalarExpression(halfExpression, Scalar.DimensionalityType.Length, update)
            Dim negative As Scalar = part.Scalars.CreateScalarExpression(negativeExpression, Scalar.DimensionalityType.Length, update)
            Dim zero As Scalar = part.Scalars.CreateScalar(0, Scalar.DimensionalityType.Length, update)
            ' Shared smart corner points: local (+/- width/2, +/- width/2, 0).
            ' Both translation and rotation remain attached to the selected CSYS.
            Dim corners As Point() = {
                part.Points.CreatePoint(csys, negative, negative, zero, update),
                part.Points.CreatePoint(csys, positive, negative, zero, update),
                part.Points.CreatePoint(csys, positive, positive, zero, update),
                part.Points.CreatePoint(csys, negative, positive, zero, update)}
            Dim edges As New List(Of Feature)()
            Dim lines As New List(Of Curve)()
            For index As Integer = 0 To 3
                Dim builder As AssociativeLineBuilder = part.BaseFeatures.CreateAssociativeLineBuilder(DirectCast(Nothing, AssociativeLine))
                Dim feature As Feature
                Try
                    builder.Associative = True
                    builder.StartPointOptions = AssociativeLineBuilder.StartOption.Point
                    builder.EndPointOptions = AssociativeLineBuilder.EndOption.Point
                    builder.StartPoint.Value = corners(index)
                    builder.EndPoint.Value = corners((index + 1) Mod 4)
                    builder.Limits.StartLimit.LimitOption = NXOpen.GeometricUtilities.CurveExtendData.LimitOptions.AtPoint
                    builder.Limits.EndLimit.LimitOption = NXOpen.GeometricUtilities.CurveExtendData.LimitOptions.AtPoint
                    If Not builder.Validate() Then Throw New InvalidOperationException("NX rejected an associative square edge.")
                    feature = builder.CommitFeature()
                Finally
                    builder.Destroy()
                End Try
                CheckFeature(feature, "Square edge")
                Dim edge As Line = Nothing
                For Each entity As NXObject In feature.GetEntities()
                    If TypeOf entity Is Line Then
                        If edge IsNot Nothing Then Throw New InvalidOperationException("Square edge returned multiple lines.")
                        edge = DirectCast(entity, Line)
                    End If
                Next
                If edge Is Nothing OrElse Math.Abs(edge.GetLength() - width) > Tolerance Then
                    Throw New InvalidOperationException("Square edge does not match the requested full side length.")
                End If
                feature.SetName("SEAL_SQUARE_EDGE_" & (index + 1).ToString())
                edges.Add(feature)
                lines.Add(edge)
            Next
            CheckCurveSamples(lines, csys, width / 2.0, WindowShape.Square)
            Dim planeBuilder As BoundedPlaneBuilder = part.Features.CreateBoundedPlaneBuilder(Nothing)
            Dim square As Feature
            Try
                planeBuilder.BoundingCurves.SetAllowedEntityTypes(Section.AllowTypes.OnlyCurves)
                planeBuilder.BoundingCurves.DistanceTolerance = Tolerance
                planeBuilder.BoundingCurves.ChainingTolerance = Tolerance
                Dim rule As CurveFeatureRule = part.ScRuleFactory.CreateRuleCurveFeature(edges.ToArray())
                planeBuilder.BoundingCurves.AddToSection(New SelectionIntentRule() {rule}, lines(0), Nothing, Nothing,
                    corners(0).Coordinates, Section.Mode.Create)
                If Not planeBuilder.Validate() Then Throw New InvalidOperationException("NX rejected the square boundary.")
                square = planeBuilder.CommitFeature()
            Finally
                planeBuilder.Destroy()
            End Try
            CheckFeature(square, "Bounded square")
            Dim bodies As Body() = square.GetBodies()
            If bodies.Length <> 1 OrElse Not bodies(0).IsSheetBody OrElse bodies(0).GetFaces().Length <> 1 Then
                Throw New InvalidOperationException("Bounded square must produce exactly one sheet with one face.")
            End If
            Dim origin As Point3d = csys.Origin
            Dim centerStatus As Integer
            UFSession.GetUFSession().Modl.AskPointContainment(New Double() {origin.X, origin.Y, origin.Z}, bodies(0).Tag, centerStatus)
            If centerStatus <> 1 Then Throw New InvalidOperationException("Square does not contain the CSYS origin in its interior.")
            square.SetName("SEAL_SQUARE")
            For Each line As Curve In lines
                line.Blank()
            Next
            For Each corner As Point In corners
                corner.Blank()
            Next
            bodies(0).Blank()
            Return bodies(0)
        End Using
    End Function

    Private Function MakeDisk(part As Part, csys As CartesianCoordinateSystem, radiusExpression As Expression, radius As Double) As Body
        Using functionTiming As IDisposable = Perf("Boundary.Circle / 円作成")
            Dim update As SmartObject.UpdateOption = SmartObject.UpdateOption.WithinModeling
            Dim zero As Scalar = part.Scalars.CreateScalar(0, Scalar.DimensionalityType.Length, update)
            Dim positive As Scalar = part.Scalars.CreateScalarExpression(radiusExpression, Scalar.DimensionalityType.Length, update)
            Dim negativeExpression As Expression = part.Expressions.CreateSystemExpressionWithUnits(
                "-" & radiusExpression.Name, DirectCast(part.UnitCollection.FindObject("MilliMeter"), Unit))
            Dim negative As Scalar = part.Scalars.CreateScalarExpression(negativeExpression, Scalar.DimensionalityType.Length, update)
            ' Three non-collinear smart points follow CSYS origin AND orientation.
            Dim start As Point = part.Points.CreatePoint(csys, positive, zero, zero, update)
            Dim middle As Point = part.Points.CreatePoint(csys, zero, positive, zero, update)
            Dim finish As Point = part.Points.CreatePoint(csys, negative, zero, zero, update)
            Dim arcBuilder As AssociativeArcBuilder = part.BaseFeatures.CreateAssociativeArcBuilder(DirectCast(Nothing, AssociativeArc))
            Dim circleFeature As Feature
            Try
                arcBuilder.Associative = True
                arcBuilder.Type = AssociativeArcBuilder.Types.ThreePointArc
                arcBuilder.StartPointOptions = AssociativeArcBuilder.StartOption.Point
                arcBuilder.MidPointOptions = AssociativeArcBuilder.MidOption.Point
                arcBuilder.EndPointOptions = AssociativeArcBuilder.EndOption.Point
                arcBuilder.StartPoint.Value = start
                arcBuilder.MidPoint.Value = middle
                arcBuilder.EndPoint.Value = finish
                arcBuilder.Limits.FullCircle = True
                If Not arcBuilder.Validate() Then Throw New InvalidOperationException("NX rejected the associative circle settings.")
                circleFeature = arcBuilder.CommitFeature()
            Finally
                arcBuilder.Destroy()
            End Try
            CheckFeature(circleFeature, "Circle")
            Dim circle As Arc = Nothing
            For Each entity As NXObject In circleFeature.GetEntities()
                If TypeOf entity Is Arc Then
                    If circle IsNot Nothing Then Throw New InvalidOperationException("Circle feature produced multiple arcs.")
                    circle = DirectCast(entity, Arc)
                End If
            Next
            If circle Is Nothing Then Throw New InvalidOperationException("Circle feature produced no arc.")
            Dim origin As Point3d = csys.Origin
            Dim actual As Point3d = circle.CenterPoint
            Dim offset As Double = Math.Sqrt((actual.X - origin.X) ^ 2 + (actual.Y - origin.Y) ^ 2 + (actual.Z - origin.Z) ^ 2)
            If offset > Tolerance OrElse Math.Abs(circle.Radius - radius) > Tolerance OrElse
                Math.Abs(circle.GetLength() - 2 * Math.PI * radius) > Tolerance Then
                Throw New InvalidOperationException("Circle does not match the requested center, radius or full circumference.")
            End If
            CheckCurveSamples(New List(Of Curve)(New Curve() {circle}), csys, radius)
            Dim planeBuilder As BoundedPlaneBuilder = part.Features.CreateBoundedPlaneBuilder(Nothing)
            Dim diskFeature As Feature
            Try
                planeBuilder.BoundingCurves.SetAllowedEntityTypes(Section.AllowTypes.OnlyCurves)
                planeBuilder.BoundingCurves.DistanceTolerance = Tolerance
                planeBuilder.BoundingCurves.ChainingTolerance = Tolerance
                Dim rule As CurveFeatureRule = part.ScRuleFactory.CreateRuleCurveFeature(New Feature() {circleFeature})
                planeBuilder.BoundingCurves.AddToSection(New SelectionIntentRule() {rule}, circle, Nothing, Nothing,
                    start.Coordinates, Section.Mode.Create)
                If Not planeBuilder.Validate() Then Throw New InvalidOperationException("NX rejected the disk boundary.")
                diskFeature = planeBuilder.CommitFeature()
            Finally
                planeBuilder.Destroy()
            End Try
            CheckFeature(diskFeature, "Bounded disk")
            Dim bodies As Body() = diskFeature.GetBodies()
            If bodies.Length <> 1 OrElse Not bodies(0).IsSheetBody OrElse bodies(0).GetFaces().Length <> 1 Then
                Throw New InvalidOperationException("Bounded Plane must produce exactly one sheet with one face.")
            End If
            Dim centerStatus As Integer
            UFSession.GetUFSession().Modl.AskPointContainment(New Double() {origin.X, origin.Y, origin.Z}, bodies(0).Tag, centerStatus)
            If centerStatus <> 1 Then Throw New InvalidOperationException("The disk does not contain its center in its face interior.")
            circleFeature.SetName("SEAL_DISK_BOUNDARY")
            diskFeature.SetName("SEAL_DISK")
            circle.Blank()
            start.Blank()
            middle.Blank()
            finish.Blank()
            bodies(0).Blank()
            Return bodies(0)
        End Using
    End Function

    Private Function IntersectLayer(part As Part, sources As List(Of Body), disk As Body, context As String) As Feature
        Using functionTiming As IDisposable = Perf("Intersection.LayerTotal / レイヤ交線全体")
            Dim session As Session = NXOpen.Session.GetSession()
            Dim localMark As Session.UndoMarkId = session.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Intersect source layer")
            Try
                Return IntersectDisk(part, sources, disk)
            Catch ex As NXException
                ' An empty category is possible even when other categories cut this disk.
                ' The exception itself is NOT evidence of an empty intersection.
                Using callTiming As IDisposable = Perf("Intersection.Rollback / 非交差試行の取消")
                    session.UndoToMark(localMark, Nothing)
                End Using
                Report(context & ": NX " & ex.ErrorCode.ToString() &
                    "; checking whether all source faces are separated from the bounded cutting sheet.")
                If FacesSeparatedFromDisk(sources, disk) Then
                    Report(context & ": no curves; every source face is separated from the cutting sheet by more than tolerance plus reported accuracy.")
                    Return Nothing
                End If
                Throw
            Finally
                session.DeleteUndoMark(localMark, Nothing)
            End Try
        End Using
    End Function

    Private Function FacesSeparatedFromDisk(sources As List(Of Body), disk As Body) As Boolean
        Using functionTiming As IDisposable = Perf("Distance.EmptyCheck / 非交差の距離確認")
            ' Measure surfaces, not solid volumes (a disk inside a solid need not cut its walls).
            Dim uf As UFSession = UFSession.GetUFSession()
            Dim diskFaces As Face() = disk.GetFaces()
            If diskFaces.Length <> 1 Then Return False
            Dim count As Integer = 0
            Try
                For Each body As Body In sources
                    Using bodyTiming As IDisposable = Perf("Distance.Body / ボディの距離確認", "body tag=" & body.Tag.ToString())
                        Dim faces As Face() = body.GetFaces()
                        If faces.Length = 0 Then Return False
                        Dim checkedFaces As Integer = 0
                        timing.Info("Distance checks: body tag=" & body.Tag.ToString() & "; faces=" & faces.Length.ToString())
                        For Each face As Face In faces
                            If checkedFaces Mod 100 = 0 Then timing.Info("Distance progress: " & perfContext & "; body tag=" & body.Tag.ToString() &
                                "; face=" & (checkedFaces + 1).ToString() & "/" & faces.Length.ToString() & "; face tag=" & face.Tag.ToString())
                            Dim distance As Double, accuracy As Double
                            Dim first(2), second(2), guess(2) As Double
                            uf.Modl.AskMinimumDist3(2, face.Tag, diskFaces(0).Tag, 0, guess, 0, guess,
                                distance, first, second, accuracy)
                            If Double.IsNaN(distance) OrElse Double.IsInfinity(distance) OrElse
                                Double.IsNaN(accuracy) OrElse Double.IsInfinity(accuracy) OrElse accuracy < 0 OrElse
                                distance <= Tolerance + accuracy Then Return False
                            Dim squared As Double = 0
                            For axis As Integer = 0 To 2
                                If Double.IsNaN(first(axis)) OrElse Double.IsInfinity(first(axis)) OrElse
                                    Double.IsNaN(second(axis)) OrElse Double.IsInfinity(second(axis)) Then Return False
                                squared += (first(axis) - second(axis)) ^ 2
                            Next
                            If Double.IsInfinity(squared) OrElse Math.Abs(Math.Sqrt(squared) - distance) > Tolerance + accuracy Then Return False
                            Dim containment As Integer
                            uf.Modl.AskPointContainment(first, face.Tag, containment)
                            If containment <> 1 AndAlso containment <> 3 Then Return False
                            uf.Modl.AskPointContainment(second, diskFaces(0).Tag, containment)
                            If containment <> 1 AndAlso containment <> 3 Then Return False
                            count += 1
                            checkedFaces += 1
                        Next
                    End Using
                Next
            Catch queryError As Exception
                Report("Could not verify empty category: " & queryError.Message)
                Return False
            End Try
            Return count > 0
        End Using
    End Function

    Private Function IntersectDisk(part As Part, sources As List(Of Body), disk As Body) As Feature
        Using functionTiming As IDisposable = Perf("Intersection.Build / 交線フィーチャ作成")
            Dim builder As IntersectionCurveBuilder = part.Features.CreateIntersectionCurveBuilder(Nothing)
            Try
                builder.Associative = True
                builder.Tolerance = Tolerance
                If builder.FirstFace Is Nothing OrElse builder.SecondFace Is Nothing Then
                    Throw New InvalidOperationException("NX did not provide the associative Intersection Curve face collectors.")
                End If
                Dim options As SelectionIntentRuleOptions = part.ScRuleFactory.CreateRuleOptions()
                Try
                    options.SetSelectedFromInactive(False)
                    Dim rules As New List(Of SelectionIntentRule)()
                    ' All faces of each sheet/solid remain in the rule set. Solid sections are
                    ' surface contours (including inner walls), not filled or hatched regions.
                    For Each body As Body In sources
                        rules.Add(part.ScRuleFactory.CreateRuleFaceBody(body, options))
                    Next
                    builder.FirstFace.ReplaceRules(rules.ToArray(), False)
                    builder.SecondFace.ReplaceRules(New SelectionIntentRule() {part.ScRuleFactory.CreateRuleFaceBody(disk, options)}, False)
                Finally
                    options.Dispose()
                End Try
                If Not builder.Validate() Then Throw New InvalidOperationException("NX rejected the cutting-sheet/body-face intersection settings.")
                Dim result As Feature
                Using commitTiming As IDisposable = Perf("Intersection.Commit / 交線計算・確定")
                    result = builder.CommitFeature()
                End Using
                CheckFeature(result, "Cutting-sheet/body-face intersection")
                Return result
            Finally
                builder.Destroy()
            End Try
        End Using
    End Function

    Private Sub CheckWindowIntersection(part As Part, window As Body, csys As CartesianCoordinateSystem, halfSize As Double, shape As WindowShape)
        Using functionTiming As IDisposable = Perf("Boundary.Test / 境界検証")
            Dim x As Vector3d
            Dim y As Vector3d
            csys.GetDirections(x, y)
            CheckWindowChord(part, window, csys, halfSize, shape, x, 2 * halfSize, "local Y")
            If shape = WindowShape.Square Then
                CheckWindowChord(part, window, csys, halfSize, shape, y, 2 * halfSize, "local X")
                Dim divisor As Double = Math.Sqrt(2.0)
                Dim diagonalNormal As New Vector3d((x.X + y.X) / divisor, (x.Y + y.Y) / divisor, (x.Z + y.Z) / divisor)
                CheckWindowChord(part, window, csys, halfSize, shape, diagonalNormal, 2 * halfSize * divisor, "diagonal")
            End If
        End Using
    End Sub

    Private Sub CheckWindowChord(part As Part, disk As Body, csys As CartesianCoordinateSystem, halfSize As Double,
                                 shape As WindowShape, normal As Vector3d, expected As Double, label As String)
        Dim session As Session = NXOpen.Session.GetSession()
        Dim mark As Session.UndoMarkId = session.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Check cutting boundary")
        Dim length As Double = 0
        Try
            Dim plane As Plane = part.Planes.CreatePlane(csys.Origin, normal, SmartObject.UpdateOption.WithinModeling)
            Dim builder As IntersectionCurveBuilder = part.Features.CreateIntersectionCurveBuilder(Nothing)
            Dim feature As Feature
            Try
                builder.Associative = True
                builder.Tolerance = Tolerance
                If builder.FirstFace Is Nothing Then Throw New InvalidOperationException("NX did not provide the disk test collector.")
                builder.FirstFace.ReplaceRules(New SelectionIntentRule() {part.ScRuleFactory.CreateRuleFaceBody(disk)}, False)
                builder.SecondPlane = plane
                feature = builder.CommitFeature()
            Finally
                builder.Destroy()
            End Try
            CheckFeature(feature, "Cutting boundary test")
            Dim curves As New List(Of Curve)()
            For Each entity As NXObject In feature.GetEntities()
                If TypeOf entity Is Curve Then
                    Dim curve As Curve = DirectCast(entity, Curve)
                    curves.Add(curve)
                    length += curve.GetLength()
                End If
            Next
            If curves.Count = 0 OrElse Math.Abs(length - expected) > Tolerance Then
                Throw New InvalidOperationException(shape.ToString() & " " & label & " test expected " & expected.ToString() & " mm. Actual total length: " & length.ToString())
            End If
            CheckCurveSamples(curves, csys, halfSize, shape)
        Finally
            session.UndoToMark(mark, Nothing)
            session.DeleteUndoMark(mark, Nothing)
        End Try
        Report(shape.ToString() & " " & label & " boundary test passed: intersection length " & length.ToString("R", CultureInfo.InvariantCulture) & " mm. Temporary test geometry removed.")
    End Sub

    Private Sub CheckFeature(feature As Feature, label As String)
        If feature Is Nothing Then Throw New InvalidOperationException(label & " returned no feature.")
        Dim errors As String() = feature.GetFeatureErrorMessages()
        Dim warnings As String() = feature.GetFeatureWarningMessages()
        If errors.Length > 0 Then Throw New InvalidOperationException(label & ": " & String.Join("; ", errors))
        If warnings.Length > 0 Then Throw New InvalidOperationException(label & " needs review: " & String.Join("; ", warnings))
    End Sub

    Private Sub CheckCurveSamples(curves As List(Of Curve), csys As CartesianCoordinateSystem, halfSize As Double,
                                  Optional shape As WindowShape = WindowShape.Circle)
        Using functionTiming As IDisposable = Perf("Curves.Validate / 曲線検証", "curves=" & curves.Count.ToString())
            ' This sanity check samples output. It is not a proof of whole-curve containment.
            ' Clipping is supplied by the bounded face passed to Intersection Curve.
            Dim origin As Point3d = csys.Origin
            Dim x As Vector3d
            Dim y As Vector3d
            csys.GetDirections(x, y)
            Dim normal As New Vector3d(x.Y * y.Z - x.Z * y.Y, x.Z * y.X - x.X * y.Z, x.X * y.Y - x.Y * y.X)
            Dim uf As UFSession = UFSession.GetUFSession()
            For Each curve As Curve In curves
                Dim evaluator As IntPtr = IntPtr.Zero
                Try
                    uf.Eval.Initialize(curve.Tag, evaluator)
                    Dim limits(1) As Double
                    uf.Eval.AskLimits(evaluator, limits)
                    For sample As Integer = 0 To 32
                        Dim point(2), derivatives(2) As Double
                        uf.Eval.Evaluate(evaluator, 0, limits(0) + (limits(1) - limits(0)) * sample / 32.0, point, derivatives)
                        Dim dx As Double = point(0) - origin.X
                        Dim dy As Double = point(1) - origin.Y
                        Dim dz As Double = point(2) - origin.Z
                        Dim localX As Double = dx * x.X + dy * x.Y + dz * x.Z
                        Dim localY As Double = dx * y.X + dy * y.Y + dz * y.Z
                        Dim localZ As Double = dx * normal.X + dy * normal.Y + dz * normal.Z
                        If Not InsideWindow(localX, localY, localZ, halfSize, shape) Then
                            Throw New InvalidOperationException("Output curve sanity check failed: sampled point lies outside the " & shape.ToString() & " or its CSYS XY plane.")
                        End If
                    Next
                Finally
                    If evaluator <> IntPtr.Zero Then uf.Eval.Free(evaluator)
                End Try
            Next
        End Using
    End Sub

    Private Function InsideWindow(x As Double, y As Double, z As Double, halfSize As Double, shape As WindowShape) As Boolean
        If Double.IsNaN(x) OrElse Double.IsInfinity(x) OrElse Double.IsNaN(y) OrElse Double.IsInfinity(y) OrElse
            Double.IsNaN(z) OrElse Double.IsInfinity(z) OrElse Math.Abs(z) > Tolerance Then Return False
        If shape = WindowShape.Square Then Return Math.Abs(x) <= halfSize + Tolerance AndAlso Math.Abs(y) <= halfSize + Tolerance
        Return Math.Sqrt(x * x + y * y) <= halfSize + Tolerance
    End Function

    Private Function CreateDrawing(part As Part, datum As DatumCsys, position As Point3d, curves As List(Of Curve), batchId As String) As NXOpen.Drawings.BaseView
        Using functionTiming As IDisposable = Perf("View.Total / ビュー全体")
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
            Dim sectionLayers As New List(Of Integer)()
            For Each curve As Curve In curves
                If Not sectionLayers.Contains(curve.Layer) Then sectionLayers.Add(curve.Layer)
            Next
            Dim builder As NXOpen.Drawings.BaseViewBuilder = part.DraftingViews.CreateBaseViewBuilder(Nothing)
            Dim view As NXOpen.Drawings.BaseView
            Try
                builder.Placement.Associative = True
                builder.Scale.ScaleType = NXOpen.Drawings.ViewScaleBuilder.Type.Ratio
                builder.Scale.Numerator = 1.0
                builder.Scale.Denominator = 1.0
                builder.SelectModelView.SelectedView = DirectCast(part.ModelingViews.FindObject("Top"), ModelingView)
                builder.SecondaryComponents.ObjectType = NXOpen.Drawings.DraftingComponentSelectionBuilder.Geometry.PrimaryGeometry
                builder.Style.ViewStyleBase.Part = part
                ' No means model curves bypass hidden-line processing, not that curves are omitted.
                builder.Style.ViewStyleHiddenLines.IncludeModelCurvesOption = NXOpen.Preferences.IncludeModelCurvesOption.No
                ' Persist exclusion in this drafting view. Do not blank bodies globally.
                For Each body As Body In part.Bodies
                    If Not builder.HiddenObjects.Objects.Contains(body) AndAlso Not builder.HiddenObjects.Objects.Add(body) Then
                        Throw New InvalidOperationException("Could not exclude a body from the curve-only view.")
                    End If
                Next
                ' HiddenObjects is retained for bodies. Individual model curves are
                ' isolated with view-dependent erasures AFTER layer visibility is set.
                builder.Style.ViewStyleOrientation.Ovt.NormalDirection =
                    part.Directions.CreateDirection(xy, zSense, SmartObject.UpdateOption.AfterModeling)
                builder.Style.ViewStyleOrientation.Ovt.XDirection =
                    part.Directions.CreateDirection(xAxis, xSense, SmartObject.UpdateOption.AfterModeling)
                builder.Placement.Placement.SetValue(Nothing, part.Views.WorkView,
                                    position)
                If Not builder.Validate() Then Throw New InvalidOperationException("NX rejected the base-view settings.")
                Using callTiming As IDisposable = Perf("View.Commit / ビュー確定")
                    view = TryCast(builder.Commit(), NXOpen.Drawings.BaseView)
                End Using
                If view Is Nothing Then Throw New InvalidOperationException("NX did not create a base view.")
            Finally
                builder.Destroy()
            End Try
            Dim states(255) As NXOpen.Layer.StateInfo
            For layerNumber As Integer = 1 To 256
                Dim visibility As NXOpen.Layer.State = NXOpen.Layer.State.Hidden
                If sectionLayers.Contains(layerNumber) Then visibility = NXOpen.Layer.State.Visible
                states(layerNumber - 1) = New NXOpen.Layer.StateInfo(layerNumber, visibility)
            Next
            Using callTiming As IDisposable = Perf("View.LayerVisibility / ビューのレイヤ表示")
                part.Layers.SetObjectsVisibilityOnLayer(view, states, False)
            End Using
            IsolateSectorCurves(part, view, curves)
            ' Rebuild drafting geometry/bounds after per-view isolation, before placement.
            Using callTiming As IDisposable = Perf("View.Update / ビュー更新")
                view.Update()
            End Using
            Dim modelPoint As Double() = {csys.Origin.X, csys.Origin.Y, csys.Origin.Z}
            ' UF expects two drawing coordinates; VB uses an inclusive upper bound.
            Dim drawingPoint(1) As Double
            UFSession.GetUFSession().View.MapModelToDrawing(view.Tag, modelPoint, drawingPoint)
            Dim reference As Point3d = view.GetDrawingReferencePoint()
            view.MoveView(New Point3d(reference.X + position.X - drawingPoint(0),
                                     reference.Y + position.Y - drawingPoint(1), 0))
            SetUniqueSectorViewName(part, view, "SECTOR_" & datum.Timestamp.ToString(CultureInfo.InvariantCulture))
            view.SetUserAttribute("SEAL_BATCH_ID", -1, batchId, Update.Option.Now)
            view.SetUserAttribute("SEAL_SECTOR_TIMESTAMP", -1, datum.Timestamp, Update.Option.Now)
            view.SetUserAttribute("SEAL_LAYER_MODE", -1, "SOURCE", Update.Option.Now)
            view.SetUserAttribute("SEAL_VISIBILITY_MODE", -1, "VIEW_DEPENDENT_ERASE", Update.Option.Now)
            Using callTiming As IDisposable = Perf("View.Update / ビュー更新")
                view.Update()
            End Using
            Return view
        End Using
    End Function

    Private Sub IsolateSectorCurves(part As Part, view As NXOpen.Drawings.DraftingView, curves As List(Of Curve))
        Using functionTiming As IDisposable = Perf("View.Isolation / ビュー表示分離")
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
            Dim excludedTags As New Dictionary(Of Tag, Boolean)()
            Dim typeCounts As New SortedDictionary(Of String, Integer)(StringComparer.Ordinal)
            Dim drawingProxyCount As Integer = 0
            Using listTiming As IDisposable = Perf("View.ExclusionList / 非表示対象の収集")
                For Each number As Integer In layers
                    For Each item As NXObject In part.Layers.GetAllObjectsOnLayer(number)
                        ' Layer enumeration also returns representations from drawing views.
                        ' They are not model geometry to erase in another member view.
                        ' Confirmed failure: sector 2, DraftingBody, NX 630035.
                        If TypeOf item Is NXOpen.Drawings.DraftingBody OrElse TypeOf item Is NXOpen.Drawings.DraftingCurve Then
                            drawingProxyCount += 1
                            Continue For
                        End If
                        Dim displayable As DisplayableObject = TryCast(item, DisplayableObject)
                        If displayable Is Nothing OrElse TypeOf displayable Is Body OrElse keep.ContainsKey(displayable.Tag) Then Continue For
                        If displayable.Tag = Tag.Null Then Throw New InvalidOperationException("Layer enumeration returned a null-tag " & displayable.GetType().FullName & " on layer " & number.ToString())
                        If excludedTags.ContainsKey(displayable.Tag) Then Continue For
                        excludedTags.Add(displayable.Tag, True)
                        excluded.Add(displayable)
                        Dim typeName As String = displayable.GetType().FullName
                        If Not typeCounts.ContainsKey(typeName) Then typeCounts.Add(typeName, 0)
                        typeCounts(typeName) += 1
                    Next
                Next
            End Using
            Report("Drafting representations excluded from model erase list: " & drawingProxyCount.ToString() & "; " & perfContext)
            For Each entry As KeyValuePair(Of String, Integer) In typeCounts
                Report("Erase candidates: " & perfContext & "; " & entry.Key & "; count=" & entry.Value.ToString())
            Next
            ' Erase is a persistent display edit in THIS view, not a geometry deletion
            ' or a global Blank(). Keep the inherited numeric layers on the curves.
            view.DependentDisplay.RemoveErasureOnObjectAndSubobjects(ownObjects.ToArray(), False)
            Using callTiming As IDisposable = Perf("View.Erase / ビュー内の非表示適用")
                If excluded.Count > 0 Then EraseWithRecovery(view, excluded.ToArray(), 0)
            End Using
            Report("View isolation: " & ownObjects.Count.ToString() &
                " sector curves; " & excluded.Count.ToString() & " other objects erased in this view.")
        End Using
    End Sub

    Private Function EraseObjectDescription(obj As DisplayableObject) As String
        If obj Is Nothing Then Return "Nothing"
        Dim description As String = obj.GetType().FullName & "; tag=" & obj.Tag.ToString()
        Try
            description &= "; layer=" & obj.Layer.ToString() & "; identifier=" & obj.JournalIdentifier
            Dim face As Face = TryCast(obj, Face)
            Dim edge As Edge = TryCast(obj, Edge)
            Dim owner As Body = Nothing
            If face IsNot Nothing Then owner = face.GetBody()
            If edge IsNot Nothing Then owner = edge.GetBody()
            If owner IsNot Nothing Then description &= "; owner body=" & owner.JournalIdentifier
        Catch metadataError As Exception
            description &= "; metadata unavailable: " & metadataError.Message
        End Try
        Return description
    End Function

    Private Sub EraseWithRecovery(view As NXOpen.Drawings.DraftingView, objects As DisplayableObject(), depth As Integer)
        ' A failed NX erase can have made partial display edits. Undo that attempt
        ' before retrying. Successful subgroups remain; the main batch mark still
        ' rolls everything back if ANY subgroup cannot be erased.
        Dim session As Session = NXOpen.Session.GetSession()
        Dim mark As Session.UndoMarkId = session.SetUndoMark(Session.MarkVisibility.Invisible, "Sector erase attempt")
        Dim split As Boolean = False
        Try
            For Each obj As DisplayableObject In objects
                If obj Is Nothing OrElse obj.Tag = Tag.Null Then Throw New InvalidOperationException(
                    "Invalid erase candidate: " & EraseObjectDescription(obj))
            Next
            view.DependentDisplay.Erase(objects)
        Catch ex As Exception
            ' Do not retry after a failed rollback; it would leave uncertain state.
            session.UndoToMark(mark, Nothing)
            Dim nxError As NXException = TryCast(ex, NXException)
            If nxError Is Nothing OrElse nxError.ErrorCode <> 630035 Then Throw
            If objects.Length = 1 Then
                Dim description As String = EraseObjectDescription(objects(0))
                Report("ERASE FAILED OBJECT: " & perfContext & "; view=" & view.Name & "; " & description)
                Throw New InvalidOperationException("Cannot isolate this drawing view. Failed erase object: " & description &
                    Environment.NewLine & "No object was skipped; the batch will be rolled back. / オブジェクトを省略せず、一括処理を取り消します。", ex)
            End If
            Report("Erase NX 630035: attempt rolled back; retrying all " & objects.Length.ToString() &
                " objects in two groups; depth=" & depth.ToString() & "; " & perfContext)
            split = True
        Finally
            session.DeleteUndoMark(mark, Nothing)
        End Try
        If Not split Then Return
        Dim middle As Integer = objects.Length \ 2
        Dim first(middle - 1) As DisplayableObject
        Dim second(objects.Length - middle - 1) As DisplayableObject
        Array.Copy(objects, 0, first, 0, first.Length)
        Array.Copy(objects, middle, second, 0, second.Length)
        EraseWithRecovery(view, first, depth + 1)
        EraseWithRecovery(view, second, depth + 1)
        Report("Erase recovery completed for " & objects.Length.ToString() & " objects; depth=" & depth.ToString())
    End Sub

    Private Sub VerifySectorCurves(view As NXOpen.Drawings.DraftingView, curves As List(Of Curve))
        Using functionTiming As IDisposable = Perf("View.Verify / ビュー表示確認")
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
            Report(view.Name & ": visibility query returned " & matched.ToString() &
                " own model curves and no unrelated model curves. Check full profiles visually.")
        End Using
    End Sub

    Private Sub SetUniqueSectorViewName(part As Part, view As NXOpen.Drawings.BaseView, baseName As String)
        ' View names can collide with views on other sheets or names NX assigned
        ' during this run. Only rename the newly created view, never an existing one.
        Dim used As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        For Each existing As NXOpen.View In part.Views
            If existing.Tag <> view.Tag Then used(existing.Name) = True
        Next
        For Each existing As ModelingView In part.ModelingViews
            If existing.Tag <> view.Tag Then used(existing.Name) = True
        Next
        For Each existing As NXOpen.Drawings.DraftingView In part.DraftingViews
            If existing.Tag <> view.Tag Then used(existing.Name) = True
        Next
        For attempt As Integer = 1 To 100
            Dim candidate As String = UnusedViewName(baseName, used)
            Try
                If Not String.Equals(view.Name, candidate, StringComparison.Ordinal) Then view.SetName(candidate)
                Report("Drawing view: " & candidate)
                Return
            Catch ex As NXException
                ' The reported NX 925035 is specifically a duplicate view name.
                ' Cover names NX reserves but does not expose in the collections.
                If ex.ErrorCode <> 925035 Then Throw
                used(candidate) = True
            End Try
        Next
        Throw New InvalidOperationException("Could not allocate a unique sector view name. / 重複しない断面ビュー名を設定できませんでした。")
    End Sub

    Private Function UnusedViewName(baseName As String, used As Dictionary(Of String, Boolean)) As String
        Dim candidate As String = baseName
        Dim suffix As Integer = 2
        While used.ContainsKey(candidate)
            candidate = baseName & "_" & suffix.ToString(CultureInfo.InvariantCulture)
            suffix += 1
        End While
        Return candidate
    End Function

    Private Sub CreateSheet(part As Part, sheetWidth As Double, sheetHeight As Double)
        Using functionTiming As IDisposable = Perf("Drawing.CreateSheet / 図面シート作成")
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
        End Using
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

' Self-contained bilingual selector; Square is first and selected on every run.
Friend NotInheritable Class SealBoundaryShapePicker
    Inherits System.Windows.Forms.Form
    Private ReadOnly choices As ComboBox
    Private ReadOnly explanation As System.Windows.Forms.Label

    Public ReadOnly Property IsSquare As Boolean
        Get
            Return choices.SelectedIndex = 0
        End Get
    End Property

    Public Sub New()
        Text = "Step 3 - Cutting shape / 手順3：切断範囲の形状"
        Font = New System.Drawing.Font("Yu Gothic UI", 10.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New System.Drawing.Size(780, 360)
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        Dim prompt As New System.Windows.Forms.Label With {.Left = 16, .Top = 12, .Width = 748, .Height = 56,
            .Text = "Choose one boundary shape for all sectors. Both are centered on each CSYS in its XY plane." & Environment.NewLine &
            "全断面に使用する形状を選択してください。どちらも各CSYSの原点を中心とし、XY平面上に作成します。"}
        choices = New ComboBox With {.Left = 16, .Top = 78, .Width = 748, .DropDownStyle = ComboBoxStyle.DropDownList}
        choices.Items.Add("Square / 正方形 (default / 既定)")
        choices.Items.Add("Circle / 円")
        explanation = New System.Windows.Forms.Label With {.Left = 16, .Top = 124, .Width = 748, .Height = 164}
        Dim ok As New Button With {.Text = "Next / 次へ", .Left = 444, .Top = 310, .Width = 150, .Height = 34, .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.Text = "Cancel / キャンセル", .Left = 604, .Top = 310, .Width = 160, .Height = 34, .DialogResult = DialogResult.Cancel}
        Controls.AddRange(New Control() {prompt, choices, explanation, ok, cancel})
        AcceptButton = ok
        CancelButton = cancel
        AddHandler choices.SelectedIndexChanged, AddressOf ExplainShape
        choices.SelectedIndex = 0
    End Sub

    Private Sub ExplainShape(sender As Object, e As EventArgs)
        If IsSquare Then
            explanation.Text = "Enter the FULL side length next (default: 50 mm). Edges follow the CSYS X/Y axes." & Environment.NewLine &
                "次に一辺の全長を入力します（既定：50 mm）。辺はCSYSのX軸・Y軸に平行です。" & Environment.NewLine & Environment.NewLine &
                "50 mm means X/Y = -25 to +25 mm, centered on the CSYS origin." & Environment.NewLine &
                "50 mmの場合、CSYS原点を中心にX/Y方向は各-25～+25 mmです。"
        Else
            explanation.Text = "Enter the RADIUS next (default: 25 mm), not the diameter." & Environment.NewLine &
                "次に半径を入力します（既定：25 mm）。直径ではありません。" & Environment.NewLine & Environment.NewLine &
                "A radius of 25 mm gives a 50 mm diameter. Only geometry within that distance is included." & Environment.NewLine &
                "半径25 mmの場合、直径は50 mmです。CSYS原点から半径以内の範囲を切断します。"
        End If
    End Sub
End Class

' File-only instrumentation: no NX calls, so log I/O failure never changes CAD results.
' Nested scopes record inclusive and exclusive times; aggregate exclusive times
' can be added without counting child operations twice.
Friend NotInheritable Class SealTimingLog
    Private ReadOnly clock As Stopwatch = Stopwatch.StartNew()
    Private writer As StreamWriter
    Private ReadOnly active As New List(Of TimingScope)()
    Private ReadOnly records As New List(Of TimingRecord)()
    Private sequence As Integer
    Public Property FilePath As String = ""
    Public Property Failure As String = ""
    Public Property Finished As Boolean
    Public Property Summary As String = ""

    Public Sub New(Optional folder As String = Nothing)
        Dim folders As New List(Of String)()
        If folder IsNot Nothing Then
            folders.Add(folder)
        Else
            folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NX_QualityOfLife", "Logs"))
            folders.Add(Path.Combine(Path.GetTempPath(), "NX_QualityOfLife", "Logs"))
        End If
        For Each candidate As String In folders
            Try
                Directory.CreateDirectory(candidate)
                Dim destination As String = Path.Combine(candidate, "SealSection_" & DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) &
                    "_" & Guid.NewGuid().ToString("N").Substring(0, 8) & ".log")
                writer = New StreamWriter(New FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read), New UTF8Encoding(True))
                writer.AutoFlush = True
                FilePath = destination
                Failure = ""
                Exit For
            Catch ex As Exception
                Failure = ex.Message
                If writer IsNot Nothing Then
                    Try
                        writer.Dispose()
                    Catch
                    End Try
                    writer = Nothing
                End If
            End Try
        Next
        WriteSafe("NX seal-section timing log v1 / 断面作成・処理時間ログ")
        WriteSafe("UTC timestamps; durations use Stopwatch. START is flushed before work begins. END does not imply success; check RUN RESULT.")
        WriteSafe("時刻はUTC、時間は秒です。STARTは処理開始前に保存します。成否はRUN RESULTを確認してください。")
        WriteSafe("utc" & vbTab & "event" & vbTab & "elapsed_s" & vbTab & "id" & vbTab & "parent" & vbTab & "step" & vbTab & "inclusive_s" & vbTab & "self_s" & vbTab & "details")
    End Sub

    Public ReadOnly Property Notice As String
        Get
            Dim text As String = "Timing log / 計測ログ: " & If(FilePath.Length = 0, "unavailable / 作成できませんでした", FilePath)
            If Failure.Length > 0 Then text &= Environment.NewLine & "Log I/O warning / ログ書込み警告: " & Failure
            Return text
        End Get
    End Property

    Public Function BeginStep(name As String, details As String) As IDisposable
        sequence += 1
        Dim parent As TimingScope = If(active.Count = 0, Nothing, active(active.Count - 1))
        Dim scope As New TimingScope(Me, sequence, parent, name, details, clock.ElapsedTicks)
        active.Add(scope)
        EventLine("START", scope, "", "")
        Return scope
    End Function

    Public Sub Info(message As String)
        If Finished Then Return
        WriteSafe(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) & vbTab & "INFO" & vbTab & Seconds(clock.ElapsedTicks) &
            vbTab & "0" & vbTab & "0" & vbTab & "message" & vbTab & vbTab & vbTab & SafeField(message))
    End Sub

    Private Sub CloseStep(scope As TimingScope, eventName As String)
        If scope.Closed Then Return
        scope.Closed = True
        Dim ticks As Long = clock.ElapsedTicks - scope.StartTicks
        Dim selfTicks As Long = Math.Max(0L, ticks - scope.ChildTicks)
        Dim inputTicks As Long = If(scope.Name.StartsWith("Input.", StringComparison.Ordinal), ticks, scope.InputTicks)
        If scope.Parent IsNot Nothing Then
            scope.Parent.ChildTicks += ticks
            scope.Parent.InputTicks += inputTicks
        End If
        active.Remove(scope)
        records.Add(New TimingRecord With {.Name = scope.Name, .Details = scope.Details,
            .Inclusive = CDbl(ticks) / Stopwatch.Frequency, .SelfTime = CDbl(selfTicks) / Stopwatch.Frequency,
            .Processing = CDbl(Math.Max(0L, ticks - inputTicks)) / Stopwatch.Frequency})
        EventLine(eventName, scope, Seconds(ticks), Seconds(selfTicks))
    End Sub

    Private Sub EventLine(eventName As String, scope As TimingScope, inclusive As String, exclusive As String)
        WriteSafe(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) & vbTab & eventName & vbTab & Seconds(clock.ElapsedTicks) &
            vbTab & scope.Id.ToString(CultureInfo.InvariantCulture) & vbTab & If(scope.Parent Is Nothing, "0", scope.Parent.Id.ToString(CultureInfo.InvariantCulture)) &
            vbTab & SafeField(scope.Name) & vbTab & inclusive & vbTab & exclusive & vbTab & SafeField(scope.Details))
    End Sub

    Private Shared Function Seconds(ticks As Long) As String
        Return (CDbl(ticks) / Stopwatch.Frequency).ToString("F6", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function SafeField(text As String) As String
        Return text.Replace(vbCr, "").Replace(vbLf, " | ").Replace(vbTab, " ")
    End Function

    Private Sub WriteSafe(text As String)
        If writer Is Nothing Then Return
        Try
            writer.WriteLine(text)
        Catch ex As Exception
            Failure = ex.Message
            Try
                writer.Dispose()
            Catch
            End Try
            writer = Nothing
        End Try
    End Sub

    Public Sub Finish(result As String)
        If Finished Then Return
        While active.Count > 0
            CloseStep(active(active.Count - 1), "INCOMPLETE")
        End While
        clock.Stop()
        Dim aggregates As New Dictionary(Of String, Aggregate)()
        Dim inputTime As Double = 0
        Dim measuredSelf As Double = 0
        For Each record As TimingRecord In records
            measuredSelf += record.SelfTime
            If record.Name.StartsWith("Input.", StringComparison.Ordinal) Then inputTime += record.SelfTime
            If Not aggregates.ContainsKey(record.Name) Then aggregates.Add(record.Name, New Aggregate With {.Name = record.Name})
            Dim total As Aggregate = aggregates(record.Name)
            total.Count += 1
            total.SelfTime += record.SelfTime
            total.Inclusive += record.Inclusive
            total.Maximum = Math.Max(total.Maximum, record.Inclusive)
        Next
        Dim ordered As New List(Of Aggregate)(aggregates.Values)
        ordered.Sort(Function(a, b) b.SelfTime.CompareTo(a.SelfTime))
        Dim slowest As New List(Of TimingRecord)(records)
        slowest.Sort(Function(a, b) b.Processing.CompareTo(a.Processing))
        Dim output As New StringBuilder()
        output.AppendLine("=== TIMING SUMMARY / 処理時間集計 ===")
        output.AppendLine("RUN RESULT / 実行結果: " & result)
        output.AppendLine("Total / 全体: " & clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) & " s; input wait / 入力待ち: " &
            inputTime.ToString("F3", CultureInfo.InvariantCulture) & " s; excluding input / 入力待ち除外: " &
            Math.Max(0, clock.Elapsed.TotalSeconds - inputTime).ToString("F3", CultureInfo.InvariantCulture) & " s")
        output.AppendLine("Unscoped overhead / 未分類・計測外: " & Math.Max(0, clock.Elapsed.TotalSeconds - measuredSelf).ToString("F3", CultureInfo.InvariantCulture) & " s")
        output.AppendLine("Stages ranked by SELF time (children excluded) / 子処理を除いた所要時間順")
        output.AppendLine("step" & vbTab & "count" & vbTab & "self_s" & vbTab & "inclusive_s" & vbTab & "max_call_s")
        For Each total As Aggregate In ordered
            If total.Name.StartsWith("Input.", StringComparison.Ordinal) Then Continue For
            output.AppendLine(total.Name & vbTab & total.Count.ToString() & vbTab & total.SelfTime.ToString("F6", CultureInfo.InvariantCulture) &
                vbTab & total.Inclusive.ToString("F6", CultureInfo.InvariantCulture) & vbTab & total.Maximum.ToString("F6", CultureInfo.InvariantCulture))
        Next
        output.AppendLine("Slowest processing calls, children included, INPUT EXCLUDED (nested; do not sum) / 入力待ち除外・子処理込み上位（重複集計に注意）")
        Dim count As Integer = 0
        For Each record As TimingRecord In slowest
            If record.Name.StartsWith("Input.", StringComparison.Ordinal) Then Continue For
            output.AppendLine(record.Processing.ToString("F6", CultureInfo.InvariantCulture) & " s" & vbTab & record.Name & vbTab & SafeField(record.Details))
            count += 1
            If count = 20 Then Exit For
        Next
        Summary = output.ToString()
        WriteSafe(Summary)
        If writer IsNot Nothing Then
            Try
                writer.Dispose()
            Catch ex As Exception
                Failure = ex.Message
            End Try
            writer = Nothing
        End If
        Finished = True
    End Sub

    Private Class TimingRecord
        Public Name As String
        Public Details As String
        Public Inclusive As Double
        Public SelfTime As Double
        Public Processing As Double
    End Class
    Private Class Aggregate
        Inherits TimingRecord
        Public Count As Integer
        Public Maximum As Double
    End Class
    Private NotInheritable Class TimingScope
        Implements IDisposable
        Private ReadOnly owner As SealTimingLog
        Public ReadOnly Id As Integer
        Public ReadOnly Parent As TimingScope
        Public ReadOnly Name As String
        Public ReadOnly Details As String
        Public ReadOnly StartTicks As Long
        Public ChildTicks As Long
        Public InputTicks As Long
        Public Closed As Boolean
        Public Sub New(log As SealTimingLog, number As Integer, parentScope As TimingScope, title As String, context As String, start As Long)
            owner = log
            Id = number
            Parent = parentScope
            Name = title
            Details = context
            StartTicks = start
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            owner.CloseStep(Me, "END")
        End Sub
    End Class
End Class
