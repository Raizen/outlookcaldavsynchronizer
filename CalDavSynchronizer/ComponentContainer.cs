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
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Markup;
using CalDavSynchronizer.AutomaticUpdates;
using CalDavSynchronizer.ChangeWatching;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Implementation.Tasks;
using CalDavSynchronizer.Implementation.Contacts;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using CalDavSynchronizer.Reports;
using CalDavSynchronizer.Scheduling;
using CalDavSynchronizer.Ui;
using CalDavSynchronizer.Ui.Reports;
using CalDavSynchronizer.Ui.Reports.ViewModels;
using CalDavSynchronizer.Utilities;
using GenSync;
using GenSync.ProgressReport;
using log4net;
using log4net.Config;
using log4net.Repository.Hierarchy;
using log4net.Core;
using Microsoft.Office.Interop.Outlook;
using Application = Microsoft.Office.Interop.Outlook.Application;
using Exception = System.Exception;
using System.Collections.Generic;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Ui.Options;
using GenSync.EntityRelationManagement;
using MessageBox = System.Windows.Forms.MessageBox;

namespace CalDavSynchronizer
{
  public class ComponentContainer: IReportsViewModelParent
  {
    public const string MessageBoxTitle = "CalDav Synchronizer";
    private static readonly ILog s_logger = LogManager.GetLogger (MethodInfo.GetCurrentMethod().DeclaringType);
    // ReSharper disable once ConvertToConstant.Local
    private readonly int c_requiredEntityCacheVersion = 1;

    private readonly object _synchronizationContextLock = new object();
    private readonly Scheduler _scheduler;
    private readonly IOptionsDataAccess _optionsDataAccess;
    private readonly IGeneralOptionsDataAccess _generalOptionsDataAccess;
    private readonly UpdateChecker _updateChecker;
    private readonly NameSpace _session;
    private readonly OutlookItemChangeWatcher _itemChangeWatcher;
    private readonly string _applicationDataDirectory;
    private readonly ISynchronizationReportRepository _synchronizationReportRepository;
    private readonly FilteringSynchronizationReportRepositoryWrapper _filteringSynchronizationReportRepository;
    private readonly IUiService _uiService;
    private ReportsViewModel _currentReportsViewModel;
    private bool _showReportsWithWarningsImmediately;
    private bool _showReportsWithErrorsImmediately;
    private readonly ReportGarbageCollection _reportGarbageCollection;
    private readonly SynchronizerFactory _synchronizerFactory;
    private readonly DaslFilterProvider _daslFilterProvider;
    private readonly IAvailableVersionService _availableVersionService;


    public event EventHandler SynchronizationFailedWhileReportsFormWasNotVisible;

    public ComponentContainer (Application application)
    {
      _uiService = new UiService();
      _generalOptionsDataAccess = new GeneralOptionsDataAccess();

      var generalOptions = _generalOptionsDataAccess.LoadOptions();

      _daslFilterProvider = new DaslFilterProvider(generalOptions.IncludeCustomMessageClasses);

      FrameworkElement.LanguageProperty.OverrideMetadata (
        typeof (FrameworkElement), 
        new FrameworkPropertyMetadata (XmlLanguage.GetLanguage (CultureInfo.CurrentCulture.IetfLanguageTag)));

      ConfigureServicePointManager (generalOptions);
      ConfigureLogLevel (generalOptions.EnableDebugLog);

      _itemChangeWatcher = new OutlookItemChangeWatcher (application.Inspectors);
      _itemChangeWatcher.ItemSavedOrDeleted += ItemChangeWatcherItemSavedOrDeleted;
      _session = application.Session;
      s_logger.Info ("Startup...");

      EnsureSynchronizationContext();

      _applicationDataDirectory = Path.Combine (
          Environment.GetFolderPath (
              generalOptions.StoreAppDataInRoamingFolder ? Environment.SpecialFolder.ApplicationData : Environment.SpecialFolder.LocalApplicationData),
          "CalDavSynchronizer");

      _optionsDataAccess = new OptionsDataAccess (
          Path.Combine (
              _applicationDataDirectory,
              GetOrCreateConfigFileName (_applicationDataDirectory, _session.CurrentProfileName)
              ));

      _synchronizerFactory = new SynchronizerFactory (
          GetProfileDataDirectory,
          new TotalProgressFactory (
              new ProgressFormFactory(),
              int.Parse (ConfigurationManager.AppSettings["loadOperationThresholdForProgressDisplay"]),
              ExceptionHandler.Instance),
          _session,
          TimeSpan.Parse (ConfigurationManager.AppSettings["calDavConnectTimeout"]),
          _daslFilterProvider);

      _synchronizationReportRepository = CreateSynchronizationReportRepository();

      _filteringSynchronizationReportRepository = new FilteringSynchronizationReportRepositoryWrapper (_synchronizationReportRepository);
      UpdateGeneralOptionDependencies(generalOptions);

      _filteringSynchronizationReportRepository.ReportAdded += _synchronizationReportRepository_ReportAdded;
      _scheduler = new Scheduler (
        _synchronizerFactory,
        _filteringSynchronizationReportRepository,
        EnsureSynchronizationContext);
      var options = _optionsDataAccess.LoadOptions();

      EnsureCacheCompatibility (options);

      _scheduler.SetOptions (options, generalOptions.CheckIfOnline);

      _availableVersionService = new AvailableVersionService();
      _updateChecker = new UpdateChecker (_availableVersionService, () => _generalOptionsDataAccess.IgnoreUpdatesTilVersion);
      _updateChecker.NewerVersionFound += UpdateChecker_NewerVersionFound;
      _updateChecker.IsEnabled = generalOptions.ShouldCheckForNewerVersions;

      _reportGarbageCollection = new ReportGarbageCollection (_synchronizationReportRepository, TimeSpan.FromDays (generalOptions.MaxReportAgeInDays));
    }

    private void EnsureCacheCompatibility (Options[] options)
    {
      var currentEntityCacheVersion = _generalOptionsDataAccess.EntityCacheVersion;

      if (currentEntityCacheVersion == 0 && c_requiredEntityCacheVersion == 1)
      {
        try
        {
          s_logger.InfoFormat ("Converting caches from 0 to 1");
          EntityCacheVersionConversion.Version0To1.Convert (
            options.Select (o => EntityRelationDataAccess.GetRelationStoragePath(GetProfileDataDirectory (o.Id))).ToArray());
          _generalOptionsDataAccess.EntityCacheVersion = c_requiredEntityCacheVersion;
        }
        catch (Exception x)
        {
          s_logger.Error ("Error during conversion. Deleting caches",x);
          if (DeleteCachesForProfiles (options.Select (p => Tuple.Create (p.Id, p.Name))))
            _generalOptionsDataAccess.EntityCacheVersion = c_requiredEntityCacheVersion;
        }
      }
      else if (currentEntityCacheVersion != c_requiredEntityCacheVersion)
      {
        s_logger.InfoFormat ("Image requires cache version '{0}',but caches have version '{1}'. Deleting caches.", c_requiredEntityCacheVersion, currentEntityCacheVersion);
        if (DeleteCachesForProfiles (options.Select (p => Tuple.Create (p.Id, p.Name))))
          _generalOptionsDataAccess.EntityCacheVersion = c_requiredEntityCacheVersion;
      }
    }

    private void UpdateGeneralOptionDependencies (GeneralOptions generalOptions)
    {
      _filteringSynchronizationReportRepository.AcceptAddingReportsWithJustWarnings = generalOptions.LogReportsWithWarnings;
      _filteringSynchronizationReportRepository.AcceptAddingReportsWithoutWarningsOrErrors = generalOptions.LogReportsWithoutWarningsOrErrors;

      _showReportsWithErrorsImmediately = generalOptions.ShowReportsWithErrorsImmediately;
      _showReportsWithWarningsImmediately = generalOptions.ShowReportsWithWarningsImmediately;

      _daslFilterProvider.SetDoIncludeCustomMessageClasses (generalOptions.IncludeCustomMessageClasses);
    }

    private void _synchronizationReportRepository_ReportAdded (object sender, ReportAddedEventArgs e)
    {
      if (IsReportsViewVisible)
      {
        ShowReports (); // show to bring it into foreground
        return;
      }

      var hasErrors = e.Report.HasErrors;
      var hasWarnings = e.Report.HasWarnings;

      if (hasErrors || hasWarnings)
      {
        if (hasWarnings && _showReportsWithWarningsImmediately
            || hasErrors && _showReportsWithErrorsImmediately)
        {
          ShowReports();
          var reportNameAsString = e.ReportName.ToString();
          _currentReportsViewModel.Reports.Single (r => r.ReportName.ToString() == reportNameAsString).IsSelected = true;
          return;
        }

        var handler = SynchronizationFailedWhileReportsFormWasNotVisible;
        if (handler != null)
          handler (this, EventArgs.Empty);
      }
    }

    private ISynchronizationReportRepository CreateSynchronizationReportRepository ()
    {
      var reportDirectory = Path.Combine (_applicationDataDirectory, "reports");

      if (!Directory.Exists (reportDirectory))
        Directory.CreateDirectory (reportDirectory);

      return new SynchronizationReportRepository (reportDirectory);
    }

    private async void ItemChangeWatcherItemSavedOrDeleted (object sender, ItemSavedEventArgs e)
    {
      try
      {
        EnsureSynchronizationContext();
        await _scheduler.RunResponsibleSynchronizationProfiles (e.EntryId, e.FolderEntryId, e.FolderStoreId);
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.LogException (x, s_logger);
      }
    }

    private static void ConfigureServicePointManager (GeneralOptions options)
    {
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11;

      if (options.EnableTls12)
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

      if (options.EnableSsl3)
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Ssl3;

      if (options.DisableCertificateValidation)
        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
      else
        ServicePointManager.ServerCertificateValidationCallback = null;
    }

    private void ConfigureLogLevel (bool debugLogLevel)
    {
      if (debugLogLevel)
      {
        ((Hierarchy)LogManager.GetRepository()).Root.Level = Level.Debug;
      }
      else
      {
        ((Hierarchy)LogManager.GetRepository()).Root.Level = Level.Info;
      }
      ((Hierarchy)LogManager.GetRepository()).RaiseConfigurationChanged (EventArgs.Empty);
    }

    public async Task SynchronizeNowNoThrow ()
    {
      try
      {
        s_logger.Info ("Synchronization manually triggered");
        EnsureSynchronizationContext();
        await _scheduler.RunNow();
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.HandleException (x, s_logger);
      }
    }

    public void ShowOptionsNoThrow ()
    {
      try
      {
        var options = _optionsDataAccess.LoadOptions();
        Options[] newOptions;
        GeneralOptions generalOptions = _generalOptionsDataAccess.LoadOptions();
        if (OptionsForm.EditOptions (
            _session,
            options,
            out newOptions,
            GetProfileDataDirectory,
            generalOptions.FixInvalidSettings ))
        {
          _optionsDataAccess.SaveOptions (newOptions);
          _scheduler.SetOptions (newOptions, generalOptions.CheckIfOnline);
          DeleteEntityChachesForChangedProfiles (options, newOptions);

          var changedOptions = CreateChangePairs (options, newOptions);

          SwitchCategories (changedOptions);
        }
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.HandleException (x, s_logger);
      }
    }

    private ChangedOptions[] CreateChangePairs (Options[] oldOptions, Options[] newOptions)
    {
      var newOptionsById = newOptions.ToDictionary (o => o.Id);

      return (
          from o in oldOptions
          let no = GetNewOptionsOrNull (o, newOptionsById)
          where no != null
          select new ChangedOptions (o, no)).ToArray ();
    }

    private void SwitchCategories (ChangedOptions[] changedOptions)
    {
      foreach (var changedOption in changedOptions)
      {
        var oldCategory = GetMappingRefPropertyOrNull<EventMappingConfiguration, string> (changedOption.Old.MappingConfiguration, o => o.EventCategory);
        var newCategory = GetMappingRefPropertyOrNull<EventMappingConfiguration, string> (changedOption.New.MappingConfiguration, o => o.EventCategory);

        if (oldCategory != newCategory && !String.IsNullOrEmpty (oldCategory))
        {
          try
          {
            SwitchCategories (changedOption, oldCategory, newCategory);
          }
          catch (Exception x)
          {
            s_logger.Error (null, x);
          }
        }

        if (!String.IsNullOrEmpty (newCategory))
        {
          var mappingConfiguration = (EventMappingConfiguration) changedOption.New.MappingConfiguration;

          if (mappingConfiguration.UseEventCategoryColorAndMapFromCalendarColor || mappingConfiguration.CategoryShortcutKey != OlCategoryShortcutKey.olCategoryShortcutKeyNone)
          {
            try
            {
              using (var categoriesWrapper = GenericComObjectWrapper.Create (_session.Categories))
              {
                foreach (var existingCategory in categoriesWrapper.Inner.ToSafeEnumerable<Category>())
                {
                  if (existingCategory.ShortcutKey == mappingConfiguration.CategoryShortcutKey)
                  {
                    existingCategory.ShortcutKey = OlCategoryShortcutKey.olCategoryShortcutKeyNone;
                  }
                }
                
                using (var categoryWrapper = GenericComObjectWrapper.Create (categoriesWrapper.Inner[newCategory]))
                {
                  if (categoryWrapper.Inner == null)
                  {
                    categoriesWrapper.Inner.Add (newCategory, mappingConfiguration.EventCategoryColor, mappingConfiguration.CategoryShortcutKey);
                  }
                  else
                  {
                    categoryWrapper.Inner.Color = mappingConfiguration.EventCategoryColor;
                    categoryWrapper.Inner.ShortcutKey = mappingConfiguration.CategoryShortcutKey;
                  }
                }
              }
            }
            catch (Exception x)
            {
              s_logger.Error (null, x);
            }
          }
        }
      }
    }

    private void SwitchCategories (ChangedOptions changedOption, string oldCategory, string newCategory)
    {
      using (var calendarFolderWrapper = GenericComObjectWrapper.Create (
          (Folder) _session.GetFolderFromID (changedOption.New.OutlookFolderEntryId, changedOption.New.OutlookFolderStoreId)))
      {
        bool isInstantSearchEnabled = false;

        using (var store = GenericComObjectWrapper.Create (calendarFolderWrapper.Inner.Store))
        {
          if (store.Inner != null) isInstantSearchEnabled = store.Inner.IsInstantSearchEnabled;
        }
        var filterBuilder = new StringBuilder (_daslFilterProvider.GetAppointmentFilter (isInstantSearchEnabled));
        OutlookEventRepository.AddCategoryFilter (filterBuilder, oldCategory);
        var eventIds = OutlookEventRepository.QueryFolder (_session, calendarFolderWrapper, filterBuilder).Select(e => e.Id);
        // todo concat Ids from cache

        foreach (var eventId in eventIds)
        {
          try
          {
            SwitchCategories (changedOption, oldCategory, newCategory, eventId);
          }
          catch (Exception x)
          {
            s_logger.Error (null, x);
          }
        }
      }
    }

    private void SwitchCategories (ChangedOptions changedOption, string oldCategory, string newCategory, string eventId)
    {
      using (var eventWrapper = new AppointmentItemWrapper (
          (AppointmentItem) _session.GetItemFromID (eventId, changedOption.New.OutlookFolderStoreId),
          entryId => (AppointmentItem) _session.GetItemFromID (entryId, changedOption.New.OutlookFolderStoreId)))
      {
        var categories = eventWrapper.Inner.Categories
            .Split (new[] { CultureInfo.CurrentCulture.TextInfo.ListSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Select (c => c.Trim());

        eventWrapper.Inner.Categories = string.Join (
            CultureInfo.CurrentCulture.TextInfo.ListSeparator,
            categories
                .Except (new[] { oldCategory })
                .Concat (new[] { newCategory })
                .Distinct());

        eventWrapper.Inner.Save();
      }
    }

    public void ShowGeneralOptionsNoThrow ()
    {
      try
      {
        var generalOptions = _generalOptionsDataAccess.LoadOptions();
        using (var optionsForm = new GeneralOptionsForm())
        {
          optionsForm.Options = generalOptions;
          if (optionsForm.Display())
          {
            var newOptions = optionsForm.Options;

            ConfigureServicePointManager (newOptions);
            ConfigureLogLevel (newOptions.EnableDebugLog);

            _updateChecker.IsEnabled = newOptions.ShouldCheckForNewerVersions;
            _reportGarbageCollection.MaxAge = TimeSpan.FromDays (newOptions.MaxReportAgeInDays);

            _generalOptionsDataAccess.SaveOptions (newOptions);
            UpdateGeneralOptionDependencies (newOptions);
            _scheduler.SetOptions (_optionsDataAccess.LoadOptions(), newOptions.CheckIfOnline);
          }
        }
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.HandleException (x, s_logger);
      }
    }
    
    private struct ChangedOptions
    {
      private readonly Options _old;
      private readonly Options _new;

      public ChangedOptions (Options old, Options @new)
          : this()
      {
        _old = old;
        _new = @new;
      }

      public Options Old
      {
        get { return _old; }
      }

      public Options New
      {
        get { return _new; }
      }
    }

    private Options GetNewOptionsOrNull (Options oldOptions, Dictionary<Guid, Options> newOptionsById)
    {
      Options newOptions;
      newOptionsById.TryGetValue (oldOptions.Id, out newOptions);
      return newOptions;
    }

    private void DeleteEntityChachesForChangedProfiles (Options[] oldOptions, Options[] newOptions)
    {
      var profilesForCacheDeletion =
          oldOptions
              .Concat (newOptions)
              .GroupBy (o => o.Id)
              .Where (g => g.GroupBy (o => new
                                           {
                                               o.OutlookFolderStoreId,
                                               o.OutlookFolderEntryId,
                                               o.CalenderUrl,
                                               o.UserName,
                                               o.DaysToSynchronizeInTheFuture,
                                               o.DaysToSynchronizeInThePast,
                                               o.IgnoreSynchronizationTimeRange
                                           }).Count() > 1)
              .Select (g => Tuple.Create (g.Key, g.First().Name))
              .ToArray();

      DeleteCachesForProfiles (profilesForCacheDeletion);
    }

    private bool DeleteCachesForProfiles (IEnumerable<Tuple<Guid,string>> profileIdWithNames)
    {
      bool allCachesDeleted = true;

      foreach (var profileIdWithName in profileIdWithNames)
      {
        try
        {
          s_logger.InfoFormat ("Deleting cache for profile '{0}' ('{1}')", profileIdWithName.Item1, profileIdWithName.Item2);

          var profileDataDirectory = GetProfileDataDirectory (profileIdWithName.Item1);
          if (Directory.Exists (profileDataDirectory))
            Directory.Delete (profileDataDirectory, true);
        }
        catch (Exception x)
        {
          s_logger.Error (null, x);
          allCachesDeleted = false;
        }
      }

      return allCachesDeleted;
    }

    private TProperty? GetMappingPropertyOrNull<TMappingConfiguration, TProperty> (MappingConfigurationBase mappingConfiguration, Func<TMappingConfiguration, TProperty> selector)
      where TMappingConfiguration : MappingConfigurationBase
      where TProperty : struct
    {
      var typedMappingConfiguration = mappingConfiguration as TMappingConfiguration;
      
      if (typedMappingConfiguration != null)
        return selector (typedMappingConfiguration);
      else
        return null;
    }

    private TProperty GetMappingRefPropertyOrNull<TMappingConfiguration, TProperty> (MappingConfigurationBase mappingConfiguration, Func<TMappingConfiguration, TProperty> selector)
      where TMappingConfiguration : MappingConfigurationBase
      where TProperty : class
    {
      var typedMappingConfiguration = mappingConfiguration as TMappingConfiguration;

      if (typedMappingConfiguration != null)
        return selector (typedMappingConfiguration);
      else
        return null;
    }


    private string GetProfileDataDirectory (Guid profileId)
    {
      return Path.Combine (
          _applicationDataDirectory,
          profileId.ToString());
    }

    private void UpdateChecker_NewerVersionFound (object sender, NewerVersionFoundEventArgs e)
    {
      EnsureSynchronizationContext();
      SynchronizationContext.Current.Send (_ => ShowGetNewVersionForm (e), null);
    }

    public async void CheckForUpdatesNowNoThrow ()
    {
      try
      {
        s_logger.Info ("CheckForUpdates manually triggered");
        EnsureSynchronizationContext ();

        var availableVersion = await Task.Run((Func<Version>)_availableVersionService.GetVersionOfDefaultDownload);
        if (availableVersion == null)
        {
          MessageBox.Show ("Did not find any default Version!", MessageBoxTitle);
          return;
        }

        var currentVersion = Assembly.GetExecutingAssembly ().GetName ().Version;
        if (availableVersion > currentVersion)
        {
          ShowGetNewVersionForm(
              new NewerVersionFoundEventArgs (
                  availableVersion,
                  _availableVersionService.GetWhatsNewNoThrow (currentVersion, availableVersion),
                  _availableVersionService.DownloadLink));
        }
        else
        {
          MessageBox.Show ("No newer Version available.", MessageBoxTitle);
        }
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.HandleException (x, s_logger);
      }
    }

    private void ShowGetNewVersionForm (NewerVersionFoundEventArgs e)
    {
      try
      {
        var form = new GetNewVersionForm (e.WhatsNewInformation, e.NewVersion, e.DownloadLink);
        form.TurnOffCheckForNewerVersions += delegate
        {
          var options = _generalOptionsDataAccess.LoadOptions();
          options.ShouldCheckForNewerVersions = false;
          _generalOptionsDataAccess.SaveOptions (options);

          MessageBox.Show ("Automatic check for newer version turned off.", MessageBoxTitle);
        };

        form.IgnoreThisVersion += delegate
        {
          _generalOptionsDataAccess.IgnoreUpdatesTilVersion = e.NewVersion;
          MessageBox.Show (string.Format ("Waiting for newer version than '{0}'.", e.NewVersion),  MessageBoxTitle);
        };

        form.ShowDialog();
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.LogException (x, s_logger);
      }
    }

    public static string GetOrCreateConfigFileName (string applicationDataDirectory, string profileName)
    {
      var profileDataAccess = new ProfileListDataAccess (Path.Combine (applicationDataDirectory, "profiles.xml"));
      var profiles = profileDataAccess.Load();
      var profile = profiles.FirstOrDefault (p => String.Compare (p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase) == 0);
      if (profile == null)
      {
        profile = new ProfileEntry()
                  {
                      ProfileName = profileName,
                      ConfigFileName = string.Format ("options_{0}.xml", Guid.NewGuid())
                  };
        profiles = profiles.Union (new[] { profile }).ToArray();
        profileDataAccess.Save (profiles);
      }
      return profile.ConfigFileName;
    }

    /// <summary>
    /// Ensures that the syncronizationcontext is not null ( it seems to be a bug that the synchronizationcontext is null in Office Addins)
    /// </summary>
    private void EnsureSynchronizationContext ()
    {
      lock (_synchronizationContextLock)
      {
        if (SynchronizationContext.Current == null)
        {
          SynchronizationContext.SetSynchronizationContext (new WindowsFormsSynchronizationContext());
        }
      }
    }

    public void ShowReportsNoThrow ()
    {
      try
      {
        EnsureSynchronizationContext();
        ShowReports();
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.HandleException (x, s_logger);
      }
    }

    private bool IsReportsViewVisible
    {
      get { return _currentReportsViewModel != null; }
    }

    private void ShowReports ()
    {
      if (_currentReportsViewModel == null)
      {
        try
        {
          _currentReportsViewModel = new ReportsViewModel (
              _synchronizationReportRepository,
              _optionsDataAccess.LoadOptions().ToDictionary (o => o.Id, o => o.Name),
              this);

          _currentReportsViewModel.ReportsClosed += delegate { _currentReportsViewModel = null; };

          _uiService.Show (_currentReportsViewModel);
        }
        catch
        {
          _currentReportsViewModel = null;
          throw;
        }
      }
      else
      {
        _currentReportsViewModel.RequireBringToFront();
      }
    }

    public void DiplayAEntity (Guid synchronizationProfileId, string entityId)
    {
      var options = GetOptionsOrNull(synchronizationProfileId);
      if (options == null)
        return;

      var item = _session.GetItemFromID (entityId, options.OutlookFolderStoreId);

      var appointment = item as AppointmentItem;
      if (appointment != null)
      {
        appointment.GetInspector.Activate ();
        return;
      }

      var task = item as TaskItem;
      if (task != null)
      {
        task.GetInspector.Activate ();
        return;
      }
      
      var contact = item as ContactItem;
      if (contact != null)
      {
        contact.GetInspector.Activate ();
        return;
      }
    }

    public async void DiplayBEntity (Guid synchronizationProfileId, string entityId)
    {
      try
      {
        var options = GetOptionsOrNull (synchronizationProfileId);
        if (options == null)
          return;

        SynchronizerFactory.AvailableSynchronizerComponents availableSynchronizerComponents;

        _synchronizerFactory.CreateSynchronizer (options,out availableSynchronizerComponents);

        if (availableSynchronizerComponents.CalDavDataAccess != null)
        {
          var entityName = new WebResourceName { Id = entityId, OriginalAbsolutePath = entityId };
          var entities = await availableSynchronizerComponents.CalDavDataAccess.GetEntities (new[] { entityName });
          DisplayFirstEntityIfAvailable (entities);
        }
        else if (availableSynchronizerComponents.CardDavDataAccess != null)
        {
          var entityName = new WebResourceName { Id = entityId, OriginalAbsolutePath = entityId };
          var entities = await availableSynchronizerComponents.CardDavDataAccess.GetEntities (new[] { entityName });
          DisplayFirstEntityIfAvailable (entities);
        }
        else
        {
          MessageBox.Show ($"The type of profile '{options.Name}' doesn't provide a way to display server entities.");
        }
      }
      catch (Exception x)
      {
        ExceptionHandler.Instance.HandleException (x, s_logger);
      }
    }

    private static void DisplayFirstEntityIfAvailable (IReadOnlyList<EntityWithId<WebResourceName, string>> entities)
    {
      if (entities.Count == 0)
      {
        MessageBox.Show ("The selected entity does not exist anymore.");
        return;
      }

      var tempFileName = Path.GetTempFileName();
      var tempTextFileName = tempFileName + ".txt";
      File.Move (tempFileName, tempTextFileName);

      File.WriteAllText (tempTextFileName, entities[0].Entity);
      System.Diagnostics.Process.Start (tempTextFileName);
    }

    private Options GetOptionsOrNull (Guid synchronizationProfileId)
    {
      var allOptions = _optionsDataAccess.LoadOptions ();
      var options = allOptions.FirstOrDefault (o => o.Id == synchronizationProfileId);
      if (options == null)
        MessageBox.Show ("The profile for the selected report doesn't exist anymore!");
      return options;
    }

  }
}