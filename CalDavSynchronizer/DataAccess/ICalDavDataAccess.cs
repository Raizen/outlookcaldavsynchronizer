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
using System.Threading.Tasks;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using GenSync;

namespace CalDavSynchronizer.DataAccess
{
  public interface ICalDavDataAccess
  {
    Task<bool> IsResourceCalender ();
    Task<bool> DoesSupportCalendarQuery ();
    Task<bool> IsCalendarAccessSupported ();
    Task<bool> IsWriteable ();

    Task<string> GetCalendarColorNoThrow ();
    Task<bool> SetCalendarColorNoThrow (string calendarColor);

    Task<IReadOnlyList<EntityVersion<WebResourceName, string>>> GetEventVersions (DateTimeRange? range);
    Task<IReadOnlyList<EntityVersion<WebResourceName, string>>> GetTodoVersions (DateTimeRange? range);
    Task<IReadOnlyList<EntityVersion<WebResourceName, string>>> GetVersions (IEnumerable<WebResourceName> eventUrls);

    Task<IReadOnlyList<EntityWithId<WebResourceName, string>>> GetEntities (IEnumerable<WebResourceName> eventUrls);

    Task<EntityVersion<WebResourceName, string>> CreateEntity (string iCalData, string uid);

    Task DeleteEntity (WebResourceName uri, string etag);
    Task<EntityVersion<WebResourceName, string>> UpdateEntity (WebResourceName url, string etag, string iCalData);
  }
}