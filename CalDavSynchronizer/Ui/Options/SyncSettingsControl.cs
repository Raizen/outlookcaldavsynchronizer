﻿// This file is Part of CalDavSynchronizer (http://outlookcaldavsynchronizer.sourceforge.net/)
// Copyright (c) 2015 Gerhard Zehetbauer
// Copyright (c) 2015 Alexander Nimmervoll
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Ui.ConnectionTests;
using log4net;
using Microsoft.Office.Interop.Outlook;

namespace CalDavSynchronizer.Ui.Options
{
  public partial class SyncSettingsControl : UserControl
  {
    private static readonly ILog s_logger = LogManager.GetLogger (MethodInfo.GetCurrentMethod().DeclaringType);

    private readonly IList<Item<int>> _availableSyncIntervals =
        (new Item<int>[] { new Item<int> (0, "Manual only") })
            .Union (Enumerable.Range (1, 2).Select (i => new Item<int> (i, i.ToString())))
            .Union (Enumerable.Range (1, 12).Select (i => i * 5).Select (i => new Item<int> (i, i.ToString()))).ToList();

    private readonly IList<Item<ConflictResolution>> _availableConflictResolutions = new List<Item<ConflictResolution>>()
                                                                                     {
                                                                                         new Item<ConflictResolution> (ConflictResolution.OutlookWins, "OutlookWins"),
                                                                                         new Item<ConflictResolution> (ConflictResolution.ServerWins, "ServerWins"),
                                                                                         //new Item<ConflictResolution> (ConflictResolution.Manual, "Manual"),
                                                                                         new Item<ConflictResolution> (ConflictResolution.Automatic, "Automatic"),
                                                                                     };


    private readonly IList<Item<SynchronizationMode>> _availableSynchronizationModes = new List<Item<SynchronizationMode>>()
                                                                                       {
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.ReplicateOutlookIntoServer, "Outlook \u2192 Server (Replicate)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.ReplicateServerIntoOutlook, "Outlook \u2190 Server (Replicate)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.MergeOutlookIntoServer, "Outlook \u2192 Server (Merge)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.MergeServerIntoOutlook, "Outlook \u2190 Server (Merge)"),
                                                                                           new Item<SynchronizationMode> (SynchronizationMode.MergeInBothDirections, "Outlook \u2190\u2192 Server (Two-Way)"),
                                                                                       };


    public SyncSettingsControl ()
    {
      InitializeComponent();

      Item.BindComboBox (_syncIntervalComboBox, _availableSyncIntervals);
      Item.BindComboBox (_conflictResolutionComboBox, _availableConflictResolutions);
      Item.BindComboBox (_synchronizationModeComboBox, _availableSynchronizationModes);

      _synchronizationModeComboBox.SelectedValueChanged += _synchronizationModeComboBox_SelectedValueChanged;
    }

    private void _synchronizationModeComboBox_SelectedValueChanged (object sender, EventArgs e)
    {
      UpdateConflictResolutionComboBoxEnabled();
    }

    private void UpdateConflictResolutionComboBoxEnabled ()
    {
      switch ((SynchronizationMode) _synchronizationModeComboBox.SelectedValue)
      {
        case SynchronizationMode.MergeInBothDirections:
          _conflictResolutionComboBox.Enabled = true;
          break;
        default:
          _conflictResolutionComboBox.Enabled = false;
          break;
      }
    }
  
    public bool SelectedModeRequiresWriteableServerResource
    {
      get { return RequiresWriteableServerResource ((SynchronizationMode) _synchronizationModeComboBox.SelectedValue); }
    }

    public string SelectedModeDisplayName 
    {
      get
      {
        var selectedMOde = (SynchronizationMode) _synchronizationModeComboBox.SelectedValue;
        return _availableSynchronizationModes.Single (m => m.Value == selectedMOde).Name;
      }
    }

    private static bool RequiresWriteableServerResource (SynchronizationMode synchronizationMode)
    {
      return synchronizationMode == SynchronizationMode.MergeInBothDirections
             || synchronizationMode == SynchronizationMode.MergeOutlookIntoServer
             || synchronizationMode == SynchronizationMode.ReplicateOutlookIntoServer;
    }

    public void SetOptions (Contracts.Options value)
    {
      numberOfDaysInThePast.Text = value.DaysToSynchronizeInThePast.ToString();
      numberOfDaysInTheFuture.Text = value.DaysToSynchronizeInTheFuture.ToString();


      _synchronizationModeComboBox.SelectedValue = value.SynchronizationMode;
      _conflictResolutionComboBox.SelectedValue = value.ConflictResolution;

      _enableTimeRangeFilteringCheckBox.Checked = !value.IgnoreSynchronizationTimeRange;
      _syncIntervalComboBox.SelectedValue = value.SynchronizationIntervalInMinutes;

      UpdateConflictResolutionComboBoxEnabled();
      UpdateTimeRangeFilteringGroupBoxEnabled();
    }

    public void FillOptions (Contracts.Options optionsToFill)
    {
      optionsToFill.DaysToSynchronizeInThePast = int.Parse (numberOfDaysInThePast.Text);
      optionsToFill.DaysToSynchronizeInTheFuture = int.Parse (numberOfDaysInTheFuture.Text);
      optionsToFill.SynchronizationMode = (SynchronizationMode) _synchronizationModeComboBox.SelectedValue;
      optionsToFill.ConflictResolution = (ConflictResolution) (_conflictResolutionComboBox.SelectedValue ?? ConflictResolution.Manual);
      optionsToFill.SynchronizationIntervalInMinutes = (int) _syncIntervalComboBox.SelectedValue;
      optionsToFill.IgnoreSynchronizationTimeRange = !_enableTimeRangeFilteringCheckBox.Checked;
    }

    private void _enableTimeRangeFilteringCheckBox_CheckedChanged (object sender, EventArgs e)
    {
      UpdateTimeRangeFilteringGroupBoxEnabled();
    }

    private void UpdateTimeRangeFilteringGroupBoxEnabled ()
    {
      _timeRangeFilteringGroupBox.Enabled = _enableTimeRangeFilteringCheckBox.Checked;
    }

    public void FixSynchronizationMode (TestResult result)
    {
      const SynchronizationMode readOnlyDefaultMode = SynchronizationMode.ReplicateServerIntoOutlook;
      if (result.ResourceType.HasFlag (ResourceType.Calendar))
      {
        if (!result.CalendarProperties.HasFlag (CalendarProperties.IsWriteable)
            && SelectedModeRequiresWriteableServerResource)
        {
          _synchronizationModeComboBox.SelectedValue = readOnlyDefaultMode;
          MessageBox.Show (
              string.Format (
                  "The specified Url is a read-only calendar. Synchronization mode set to '{0}'.",
                  _availableSynchronizationModes.Single (m => m.Value == readOnlyDefaultMode).Name),
              OptionTasks.ConnectionTestCaption);
        }
      }

      if (result.ResourceType.HasFlag (ResourceType.AddressBook))
      {
        if (!result.AddressBookProperties.HasFlag (AddressBookProperties.IsWriteable)
            && SelectedModeRequiresWriteableServerResource)
        {
          _synchronizationModeComboBox.SelectedValue = readOnlyDefaultMode;
          MessageBox.Show (
              string.Format (
                  "The specified Url is a read-only addressbook. Synchronization mode set to '{0}'.",
                  _availableSynchronizationModes.Single (m => m.Value == readOnlyDefaultMode).Name),
              OptionTasks.ConnectionTestCaption);
        }
      }
    }

    public void FixTimeRangeUsage (OlItemType? folderType)
    {
      if (folderType == OlItemType.olContactItem)
      {
        _enableTimeRangeFilteringCheckBox.Checked = false;
        UpdateTimeRangeFilteringGroupBoxEnabled();
      }
      else
      {
        _enableTimeRangeFilteringCheckBox.Checked = true;
        UpdateTimeRangeFilteringGroupBoxEnabled();
      }
    }
  }
}