Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms
Imports NXOpen
Imports NXOpen.Features
Imports NXOpenUI

' Siemens NX 2306 - Studio Spline (Through Points) from a Feature Group.
' Use Tools > Journal > Play, or assign to an NX ribbon button (Journal File).
' - Reads actual NX Point objects belonging to features inside the selected group.
' - Orders group members by ascending feature timestamp (Part Navigator order).
' - Degree = 3, open spline, G1/G2/G3 = None.
' - The new spline references the source point features associatively.
' - No new visible points are created; original point features are not modified.
Module SplineThroughGroupPoints

    Sub Main()
        Dim session As Session = Session.GetSession()
        Dim nxUI As NXOpen.UI = NXOpen.UI.GetUI()
        Dim workPart As Part = session.Parts.Work

        If workPart Is Nothing Then
            Message(nxUI, "Open a work part first.", True)
            Return
        End If

        Dim groups As New List(Of FeatureGroup)()
        Dim groupNames As New List(Of String)()
        For Each feat As Feature In workPart.Features.GetFeatures()
            If TypeOf feat Is FeatureGroup Then
                Dim featureGroup As FeatureGroup = DirectCast(feat, FeatureGroup)
                If Not String.IsNullOrWhiteSpace(featureGroup.Name) Then
                    groups.Add(featureGroup)
                    groupNames.Add(featureGroup.Name)
                End If
            End If
        Next

        If groups.Count = 0 Then
            Message(nxUI, "No named feature groups found in the work part.", True)
            Return
        End If

        groupNames.Sort(StringComparer.OrdinalIgnoreCase)
        Dim defaultName As String = groupNames(0)

        Dim selectedName As String = Nothing
        Using picker As New SplineFeatureGroupPicker(groupNames, defaultName)
            NXOpenUI.FormUtilities.SetApplicationIcon(picker)
            NXOpenUI.FormUtilities.ReparentForm(picker)
            If picker.ShowDialog() <> DialogResult.OK Then Return
            selectedName = picker.SelectedGroupName
        End Using

        If String.IsNullOrWhiteSpace(selectedName) Then Return
        selectedName = selectedName.Trim()

        Dim selectedGroup As FeatureGroup = Nothing
        Dim matchCount As Integer = 0
        For Each grp As FeatureGroup In groups
            If String.Equals(grp.Name, selectedName, StringComparison.OrdinalIgnoreCase) Then
                selectedGroup = grp
                matchCount += 1
            End If
        Next
        If matchCount <> 1 Then
            Message(nxUI, "Expected exactly one group named '" & selectedName &
                    "'. Found: " & matchCount.ToString() & ".", True)
            Return
        End If

        ' Snapshot members BEFORE creating any new feature.
        Dim members() As Feature = Nothing
        selectedGroup.GetMembers(members)
        If members Is Nothing OrElse members.Length = 0 Then
            Message(nxUI, "This feature group is empty.", True)
            Return
        End If

        Dim ordered As New List(Of Feature)(members)
        ordered.Sort(Function(a As Feature, b As Feature) a.Timestamp.CompareTo(b.Timestamp))

        ' Get the original point objects from the group member features.
        ' PointFeature.GetEntities() returns its NXOpen.Point.
        Dim sourcePoints As New List(Of NXOpen.Point)()
        For Each feat As Feature In ordered
            If feat.Suppressed Then Continue For
            For Each entity As NXObject In feat.GetEntities()
                If TypeOf entity Is NXOpen.Point Then
                    sourcePoints.Add(DirectCast(entity, NXOpen.Point))
                End If
            Next
        Next

        If sourcePoints.Count < 4 Then
            Message(nxUI, "A degree-3 spline requires at least four points." &
                    Environment.NewLine & "Group '" & selectedGroup.Name & "' contains " &
                    sourcePoints.Count.ToString() & " usable point(s).", True)
            Return
        End If

        Dim undoMark As Session.UndoMarkId = session.SetUndoMark(
            Session.MarkVisibility.Visible, "Spline Through Feature Group Points")
        Dim builder As StudioSplineBuilderEx = Nothing

        Try
            builder = workPart.Features.CreateStudioSplineBuilderEx(Nothing)
            builder.Type = StudioSplineBuilderEx.Types.ThroughPoints
            builder.Degree = 3
            builder.IsPeriodic = False
            builder.IsAssociative = True
            builder.MatchKnotsType = StudioSplineBuilderEx.MatchKnotsTypes.None
            builder.HasPlaneConstraint = False

            ' Zero offset makes an associative *copy* of each source point.
            ' Passing the source Point directly to the constraint manager is NOT
            ' recommended: NX says constraint points are owned by the spline feature.
            ' Do not use CreatePoint(sourcePoint.Coordinates) or RemoveParameters.
            Dim zeroOffset As NXOpen.Offset = workPart.Offsets.CreateOffset(
                New NXOpen.Point3d(0.0, 0.0, 0.0),
                New NXOpen.Vector3d(0.0, 0.0, 0.0),
                SmartObject.UpdateOption.WithinModeling)

            For Each parentPoint As NXOpen.Point In sourcePoints
                Dim refPoint As NXOpen.Point = workPart.Points.CreatePoint(
                    zeroOffset, parentPoint, SmartObject.UpdateOption.WithinModeling)
                refPoint.SetVisibility(SmartObject.VisibilityOption.Invisible)

                Dim constraint As GeometricConstraintData =
                    builder.ConstraintManager.CreateGeometricConstraintData()
                constraint.Point = refPoint
                constraint.AutomaticConstraintType = GeometricConstraintData.AutoConstraintType.None
                constraint.AutomaticConstraintDirection = GeometricConstraintData.ParameterDirection.Iso
                constraint.TangentDirection = Nothing
                constraint.TangentMagnitude = Nothing
                constraint.Curvature = Nothing
                constraint.CurvatureDerivative = Nothing
                constraint.HasSymmetricModelingConstraint = False
                builder.ConstraintManager.Append(constraint)
            Next

            Dim newFeature As Feature = builder.CommitFeature()
            If newFeature Is Nothing Then
                Throw New InvalidOperationException("NX did not return a Studio Spline feature.")
            End If
            newFeature.SetName("SPLINE_" & selectedGroup.Name)

            session.UpdateManager.DoUpdate(undoMark)
            Message(nxUI, "Created one ASSOCIATIVE, degree-3 Studio Spline from " &
                    sourcePoints.Count.ToString() & " points in '" & selectedGroup.Name & "'." &
                    Environment.NewLine & "Order: Part Navigator top to bottom." &
                    Environment.NewLine & "G1 / G2 / G3: None. Closed: Off.", False)

        Catch ex As Exception
            Try
                session.UndoToMark(undoMark, "Spline Through Feature Group Points")
            Catch
                ' If rollback fails, use NX Undo to remove the partial result.
            End Try
            Message(nxUI, "Spline creation failed: " & ex.Message &
                    Environment.NewLine & "Check the Part Navigator / Undo history.", True)
        Finally
            If builder IsNot Nothing Then builder.Destroy()
        End Try
    End Sub

    Private Sub Message(ui As NXOpen.UI, content As String, isError As Boolean)
        Dim kind As NXMessageBox.DialogType = NXMessageBox.DialogType.Information
        If isError Then kind = NXMessageBox.DialogType.Error
        ui.NXMessageBox.Show("Spline Through Group Points", kind, content)
    End Sub

    Public Function GetUnloadOption(ByVal dummy As String) As Integer
        Return CInt(Session.LibraryUnloadOption.Immediately)
    End Function
End Module

' Small DPI-aware combo-box dialog, matching the previous Arc Center Points journal.
Friend NotInheritable Class SplineFeatureGroupPicker
    Inherits System.Windows.Forms.Form

    Private ReadOnly groupSelector As ComboBox

    Public ReadOnly Property SelectedGroupName As String
        Get
            Return groupSelector.Text.Trim()
        End Get
    End Property

    Public Sub New(names As IEnumerable(Of String), defaultName As String)
        MyBase.New()
        Me.Text = "Spline Through Group Points"
        Me.Font = New Font("Segoe UI", 10.0F, FontStyle.Regular)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(460, 190)
        Me.MinimumSize = New Size(390, 205)
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
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 44.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))

        Dim prompt As New Label()
        prompt.Text = "Select a feature group containing points:"
        prompt.Dock = DockStyle.Fill
        prompt.TextAlign = ContentAlignment.MiddleLeft
        layout.Controls.Add(prompt, 0, 0)

        groupSelector = New ComboBox()
        groupSelector.Name = "FeatureGroupSelector"
        groupSelector.Dock = DockStyle.Top
        groupSelector.DropDownStyle = ComboBoxStyle.DropDown
        groupSelector.AutoCompleteMode = AutoCompleteMode.SuggestAppend
        groupSelector.AutoCompleteSource = AutoCompleteSource.ListItems
        groupSelector.IntegralHeight = False
        groupSelector.MaxDropDownItems = 12
        For Each name As String In names
            groupSelector.Items.Add(name)
        Next
        groupSelector.SelectedItem = defaultName
        layout.Controls.Add(groupSelector, 0, 1)

        Dim hint As New Label()
        hint.Text = "Creates one associative, degree-3 spline in navigator order."
        hint.Dock = DockStyle.Fill
        hint.TextAlign = ContentAlignment.MiddleLeft
        hint.ForeColor = SystemColors.GrayText
        layout.Controls.Add(hint, 0, 2)

        Dim buttons As New FlowLayoutPanel()
        buttons.Dock = DockStyle.Fill
        buttons.FlowDirection = FlowDirection.RightToLeft
        buttons.WrapContents = False
        buttons.Margin = New Padding(0)

        Dim cancelButton As New Button()
        cancelButton.Text = "Cancel"
        cancelButton.DialogResult = DialogResult.Cancel
        cancelButton.Size = New Size(96, 32)
        buttons.Controls.Add(cancelButton)

        Dim okButton As New Button()
        okButton.Text = "OK"
        okButton.DialogResult = DialogResult.OK
        okButton.Size = New Size(96, 32)
        buttons.Controls.Add(okButton)

        layout.Controls.Add(buttons, 0, 3)
        Me.Controls.Add(layout)
        Me.AcceptButton = okButton
        Me.CancelButton = cancelButton
        Me.ActiveControl = groupSelector
    End Sub
End Class
