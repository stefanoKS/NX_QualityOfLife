Option Strict On
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms
Imports NXOpen
Imports NXOpen.Features
Imports NXOpenUI

' NX 2306 / Windows: Associative arc-center point features.
' Play as a VB.NET journal, or assign it to a
' custom ribbon button whose action type is "Journal File".
Module ArcCenterPoints

    Sub Main()
        Dim session As Session = Session.GetSession()
        Dim nxUI As NXOpen.UI = NXOpen.UI.GetUI()
        Dim workPart As Part = session.Parts.Work

        If workPart Is Nothing Then
            ShowMessage(nxUI, "Open a work part first.", True)
            Return
        End If

        ' Only groups belonging to the current work part are considered.
        Dim groups As New List(Of FeatureGroup)()
        Dim groupNames As New List(Of String)()
        For Each feat As Feature In workPart.Features.GetFeatures()
            If TypeOf feat Is FeatureGroup Then
                Dim grp As FeatureGroup = DirectCast(feat, FeatureGroup)
                If Not String.IsNullOrWhiteSpace(grp.Name) Then
                    groups.Add(grp)
                    groupNames.Add(grp.Name)
                End If
            End If
        Next

        If groups.Count = 0 Then
            ShowMessage(nxUI, "No named feature groups were found in the work part.", True)
            Return
        End If

        groupNames.Sort(StringComparer.OrdinalIgnoreCase)
        Dim defaultName As String = groupNames(0)
        For Each name As String In groupNames
            If String.Equals(name, "CLIP_CIRCLE", StringComparison.OrdinalIgnoreCase) Then
                defaultName = name
                Exit For
            End If
        Next

        ' Use a Windows Forms combo box rather than NXInputBox.
        ' Its dropdown contains only named groups from the current work part.
        ' The text is also editable, so an existing group can be typed directly.
        Dim enteredName As String = Nothing
        Using picker As New FeatureGroupPicker(groupNames, defaultName)
            NXOpenUI.FormUtilities.SetApplicationIcon(picker)
            NXOpenUI.FormUtilities.ReparentForm(picker)
            If picker.ShowDialog() <> DialogResult.OK Then Return
            enteredName = picker.SelectedGroupName
        End Using

        If String.IsNullOrWhiteSpace(enteredName) Then Return
        enteredName = enteredName.Trim()

        Dim selectedGroup As FeatureGroup = Nothing
        Dim matches As Integer = 0
        For Each grp As FeatureGroup In groups
            If String.Equals(grp.Name, enteredName, StringComparison.OrdinalIgnoreCase) Then
                selectedGroup = grp
                matches += 1
            End If
        Next

        If matches <> 1 Then
            ShowMessage(nxUI, "Expected exactly one feature group named '" & enteredName & _
                        "'. Found: " & matches.ToString(), True)
            Return
        End If

        ' Take a snapshot before adding new point features.
        Dim members() As Feature = Nothing
        selectedGroup.GetMembers(members)
        If members Is Nothing OrElse members.Length = 0 Then
            ShowMessage(nxUI, "The selected feature group is empty.", True)
            Return
        End If

        ' Ascending NX feature timestamp matches the top-to-bottom sequence
        ' shown in the provided Part Navigator example: Arc (143), (144), ...
        Dim ordered As New List(Of Feature)(members)
        ordered.Sort(Function(a As Feature, b As Feature) a.Timestamp.CompareTo(b.Timestamp))

        ' Collect actual source arc objects, not their current XYZ centers.
        ' Keeping the object references lets NX establish parent associativity.
        Dim sourceArcs As New List(Of NXOpen.Arc)()
        For Each feat As Feature In ordered
            If feat.Suppressed Then Continue For
            For Each entity As NXObject In feat.GetEntities()
                If TypeOf entity Is NXOpen.Arc Then
                    sourceArcs.Add(DirectCast(entity, NXOpen.Arc))
                End If
            Next
        Next

        If sourceArcs.Count = 0 Then
            ShowMessage(nxUI, "No arc geometry was found directly in '" & _
                        selectedGroup.Name & "'. Nothing was created.", True)
            Return
        End If

        Dim undoMark As Session.UndoMarkId = session.SetUndoMark( _
            Session.MarkVisibility.Visible, "Create Associative Arc Center Points")

        Try
            For index As Integer = 0 To sourceArcs.Count - 1
                ' This is the NX "Arc/Ellipse/Sphere Center" point type.
                ' Do NOT use the arc's CenterPoint coordinates or RemoveParameters:
                ' doing so would sever the dependency on the parent arc.
                Dim parentArc As NXOpen.IBaseCurve = DirectCast(sourceArcs(index), NXOpen.IBaseCurve)
                Dim centerPoint As NXOpen.Point = workPart.Points.CreatePoint( _
                    parentArc, SmartObject.UpdateOption.WithinModeling)
                centerPoint.SetVisibility(SmartObject.VisibilityOption.Visible)

                ' Commit a real point *feature* to the Part Navigator.
                Dim builder As PointFeatureBuilder = Nothing
                Try
                    builder = workPart.BaseFeatures.CreatePointFeatureBuilder(Nothing)
                    builder.Point = centerPoint
                    Dim newFeature As Feature = builder.CommitFeature()
                    newFeature.SetName("ARC_CENTER_" & (index + 1).ToString("000"))
                Finally
                    If builder IsNot Nothing Then builder.Destroy()
                End Try
            Next

            session.UpdateManager.DoUpdate(undoMark)
            ShowMessage(nxUI, sourceArcs.Count.ToString() & " ASSOCIATIVE point features created from '" & _
                        selectedGroup.Name & "' in top-to-bottom feature order." & _
                        Environment.NewLine & "Each point references its source arc center.", False)

        Catch ex As Exception
            ' Avoid leaving an incomplete series of points behind.
            Try
                session.UndoToMark(undoMark, "Create Associative Arc Center Points")
            Catch
                ' If rollback itself fails, tell the user to use NX Undo.
            End Try
            ShowMessage(nxUI, "Point creation failed: " & ex.Message & _
                        Environment.NewLine & "Check the Part Navigator / Undo history.", True)
        End Try
    End Sub

    Private Sub ShowMessage(nxUI As NXOpen.UI, text As String, isError As Boolean)
        Dim dialogType As NXMessageBox.DialogType = NXMessageBox.DialogType.Information
        If isError Then dialogType = NXMessageBox.DialogType.Error
        nxUI.NXMessageBox.Show("Arc Center Points", dialogType, text)
    End Sub

    Public Function GetUnloadOption(ByVal dummy As String) As Integer
        Return CInt(Session.LibraryUnloadOption.Immediately)
    End Function

End Module

' Self-contained Windows Forms dialog for NX journals; no designer file needed.
' A layout panel and Segoe UI scaling avoid text overlap at higher DPI.
Friend NotInheritable Class FeatureGroupPicker
    Inherits System.Windows.Forms.Form

    Private ReadOnly groupSelector As ComboBox

    Public ReadOnly Property SelectedGroupName As String
        Get
            Return groupSelector.Text.Trim()
        End Get
    End Property

    Public Sub New(names As IEnumerable(Of String), defaultName As String)
        MyBase.New()

        Me.Text = "Create Arc Center Points"
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

        Dim label As New Label()
        label.Text = "Select a feature group:"
        label.Dock = DockStyle.Fill
        label.TextAlign = ContentAlignment.MiddleLeft
        layout.Controls.Add(label, 0, 0)

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
        hint.Text = "Select from the list, or type an existing group name."
        hint.Dock = DockStyle.Fill
        hint.TextAlign = ContentAlignment.MiddleLeft
        hint.ForeColor = SystemColors.GrayText
        layout.Controls.Add(hint, 0, 2)

        Dim buttonRow As New FlowLayoutPanel()
        buttonRow.Dock = DockStyle.Fill
        buttonRow.FlowDirection = FlowDirection.RightToLeft
        buttonRow.WrapContents = False
        buttonRow.Margin = New Padding(0)

        Dim cancelButton As New Button()
        cancelButton.Text = "Cancel"
        cancelButton.DialogResult = DialogResult.Cancel
        cancelButton.Size = New Size(96, 32)
        buttonRow.Controls.Add(cancelButton)

        Dim okButton As New Button()
        okButton.Text = "OK"
        okButton.DialogResult = DialogResult.OK
        okButton.Size = New Size(96, 32)
        buttonRow.Controls.Add(okButton)
        layout.Controls.Add(buttonRow, 0, 3)

        Me.Controls.Add(layout)
        Me.AcceptButton = okButton
        Me.CancelButton = cancelButton
        Me.ActiveControl = groupSelector
    End Sub
End Class
