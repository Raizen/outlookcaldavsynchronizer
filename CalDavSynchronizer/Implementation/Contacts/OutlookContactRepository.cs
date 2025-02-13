// This file is Part of CalDavSynchronizer (http://outlookcaldavsynchronizer.sourceforge.net/)
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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using GenSync;
using GenSync.EntityRepositories;
using GenSync.Logging;
using Microsoft.Office.Interop.Outlook;
using System.Runtime.InteropServices;
using log4net;

namespace CalDavSynchronizer.Implementation.Contacts
{
  public class OutlookContactRepository : IEntityRepository<ContactItemWrapper, string, DateTime>, IOutlookRepository
  {
    private static readonly ILog s_logger = LogManager.GetLogger (System.Reflection.MethodInfo.GetCurrentMethod().DeclaringType);

    private readonly NameSpace _mapiNameSpace;
    private readonly string _folderId;
    private readonly string _folderStoreId;
    private readonly IDaslFilterProvider _daslFilterProvider;
    
    private const string PR_ASSOCIATED_BIRTHDAY_APPOINTMENT_ID = "http://schemas.microsoft.com/mapi/id/{00062004-0000-0000-C000-000000000046}/804D0102";

    public OutlookContactRepository (NameSpace mapiNameSpace, string folderId, string folderStoreId, IDaslFilterProvider daslFilterProvider)
    {
      if (mapiNameSpace == null)
        throw new ArgumentNullException (nameof (mapiNameSpace));
      if (daslFilterProvider == null)
        throw new ArgumentNullException (nameof (daslFilterProvider));

      _mapiNameSpace = mapiNameSpace;
      _folderId = folderId;
      _folderStoreId = folderStoreId;
      _daslFilterProvider = daslFilterProvider;
    }

    private const string c_entryIdColumnName = "EntryID";


    private GenericComObjectWrapper<Folder> CreateFolderWrapper ()
    {
      return GenericComObjectWrapper.Create ((Folder) _mapiNameSpace.GetFolderFromID (_folderId, _folderStoreId));
    }

    public Task<IReadOnlyList<EntityVersion<string, DateTime>>> GetVersions (IEnumerable<IdWithAwarenessLevel<string>> idsOfEntitiesToQuery)
    {
      return Task.FromResult<IReadOnlyList<EntityVersion<string, DateTime>>> (
          idsOfEntitiesToQuery
              .Select (id => (ContactItem) _mapiNameSpace.GetItemFromID (id.Id, _folderStoreId))
              .ToSafeEnumerable()
              .Select (c => EntityVersion.Create (c.EntryID, c.LastModificationTime))
              .ToList());
    }

    public Task<IReadOnlyList<EntityVersion<string, DateTime>>> GetAllVersions (IEnumerable<string> idsOfknownEntities)
    {
      var contacts = new List<EntityVersion<string, DateTime>>();


      using (var addressbookFolderWrapper = CreateFolderWrapper())
      {
        bool isInstantSearchEnabled = false;

        using (var store = GenericComObjectWrapper.Create (addressbookFolderWrapper.Inner.Store))
        {
          if (store.Inner != null) isInstantSearchEnabled = store.Inner.IsInstantSearchEnabled;
        }
        using (var tableWrapper = 
          GenericComObjectWrapper.Create (addressbookFolderWrapper.Inner.GetTable (_daslFilterProvider.GetContactFilter (isInstantSearchEnabled))))
        {
          var table = tableWrapper.Inner;
          table.Columns.RemoveAll();
          table.Columns.Add (c_entryIdColumnName);

          var storeId = addressbookFolderWrapper.Inner.StoreID;

          while (!table.EndOfTable)
          {
            var row = table.GetNextRow();
            var entryId = (string) row[c_entryIdColumnName];
            try
            {
              using (var contactWrapper = GenericComObjectWrapper.Create ((ContactItem)_mapiNameSpace.GetItemFromID (entryId, storeId)))
              {
                contacts.Add (new EntityVersion<string, DateTime> (contactWrapper.Inner.EntryID, contactWrapper.Inner.LastModificationTime));
              }
            }
            catch (COMException ex)
            {
              s_logger.Error ("Could not fetch ContactItem, skipping.", ex);
            }
          }
        }
      }

      return Task.FromResult<IReadOnlyList<EntityVersion<string, DateTime>>> (contacts);
    }

    private static readonly CultureInfo _currentCultureInfo = CultureInfo.CurrentCulture;

    private string ToOutlookDateString (DateTime value)
    {
      return value.ToString ("g", _currentCultureInfo);
    }

#pragma warning disable 1998
    public async Task<IReadOnlyList<EntityWithId<string, ContactItemWrapper>>> Get (ICollection<string> ids, ILoadEntityLogger logger)
#pragma warning restore 1998
    {
      return ids
          .Select (id => EntityWithId.Create (
              id,
              new ContactItemWrapper (
                  (ContactItem) _mapiNameSpace.GetItemFromID (id, _folderStoreId),
                  entryId => (ContactItem)_mapiNameSpace.GetItemFromID (entryId, _folderStoreId))))
          .ToArray();
    }

    public void Cleanup (IReadOnlyDictionary<string, ContactItemWrapper> entities)
    {
      foreach (var contactItemWrapper in entities.Values)
        contactItemWrapper.Dispose();
    }

    public Task<EntityVersion<string, DateTime>> Update (
      string entityId, 
      DateTime entityVersion,
      ContactItemWrapper entityToUpdate, Func<ContactItemWrapper, ContactItemWrapper> entityModifier)
    {
      entityToUpdate = entityModifier (entityToUpdate);
      entityToUpdate.Inner.Save();
      return Task.FromResult (new EntityVersion<string, DateTime> (entityToUpdate.Inner.EntryID, entityToUpdate.Inner.LastModificationTime));
    }

    public Task Delete (string entityId,DateTime version)
    {
      var entityWithId = Get (new[] { entityId }, NullLoadEntityLogger.Instance).Result.SingleOrDefault ();
      if (entityWithId == null)
        return Task.FromResult (0);

      using (var contact = entityWithId.Entity)
      {
        if (!contact.Inner.Birthday.Equals (new DateTime(4501, 1, 1, 0, 0, 0)))
        {
          try
          {
            Byte[] ba = contact.Inner.GetPropertySafe (PR_ASSOCIATED_BIRTHDAY_APPOINTMENT_ID);
            string birthdayAppointmentItemID = BitConverter.ToString (ba).Replace ("-", string.Empty);
            AppointmentItemWrapper birthdayWrapper = new AppointmentItemWrapper ((AppointmentItem)_mapiNameSpace.GetItemFromID (birthdayAppointmentItemID), 
                                                                                  entryId => (AppointmentItem)_mapiNameSpace.GetItemFromID (birthdayAppointmentItemID));
            birthdayWrapper.Inner.Delete();
          }
          catch (COMException ex)
          {
            s_logger.Error ("Could not delete associated Birthday Appointment.", ex);
          }
        }
        contact.Inner.Delete();
      }

      return Task.FromResult (0);
    }

    public Task<EntityVersion<string, DateTime>> Create (Func<ContactItemWrapper, ContactItemWrapper> entityInitializer)
    {
      ContactItemWrapper newWrapper;

      using (var folderWrapper = CreateFolderWrapper())
      {
        newWrapper = new ContactItemWrapper (
          (ContactItem)folderWrapper.Inner.Items.Add (OlItemType.olContactItem),
          entryId => (ContactItem)_mapiNameSpace.GetItemFromID (entryId, _folderStoreId));
      }

      using (newWrapper)
      {
        using (var initializedWrapper = entityInitializer (newWrapper))
        {
          initializedWrapper.SaveAndReload();
          var result = new EntityVersion<string, DateTime> (initializedWrapper.Inner.EntryID, initializedWrapper.Inner.LastModificationTime);
          return Task.FromResult (result);
        }
      }
    }

    public bool IsResponsibleForFolder (string folderEntryId, string folderStoreId)
    {
      return folderEntryId == _folderId && folderStoreId == _folderStoreId;
    }
  }
}