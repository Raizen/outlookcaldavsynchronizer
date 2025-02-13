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
using System.Net;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Threading.Tasks;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Scheduling;
using CalDavSynchronizer.Contracts;

namespace CalDavSynchronizer.Ui.ConnectionTests
{
  public static class ConnectionTester
  {
    public static bool IsOnline (ProxyOptions proxyOptions)
    {
      if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
      {
        // Use NCSI to detect network status according to https://technet.microsoft.com/en-us/library/ee126135(v=WS.10).aspx
        // try DNS first
        try
        {
          IPHostEntry hostEntry = Dns.GetHostEntry ("dns.msftncsi.com");
          IPAddress ipAddress = Array.Find (hostEntry.AddressList, ip => ip.AddressFamily == AddressFamily.InterNetwork);
          if (ipAddress != null && ipAddress.ToString() == "131.107.255.255") return true;
        }
        catch (Exception) {}
        // if DNS failed, try to download the ncsi.txt
        try
        {
          string txt;
          using (var client = new WebClient())
          {
            IWebProxy proxy = (proxyOptions != null) ? SynchronizerFactory.CreateProxy (proxyOptions) : null;
            client.Proxy = proxy;
            txt = client.DownloadString (new Uri ("http://www.msftncsi.com/ncsi.txt"));
          }
          if (txt != "Microsoft NCSI") return false;
          return true;
        }
        catch (Exception)
        {
          return false;
        }
      }
      else
        return false;
    }

    public static async Task<TestResult> TestConnection (Uri url, IWebDavClient webDavClient, ResourceType supposedRessourceType)
    {
      var calDavDataAccess = new CalDavDataAccess (url, webDavClient);
      var cardDavDataAccess = new CardDavDataAccess (url, webDavClient);

      TestResult result;


      switch (supposedRessourceType)
      {
        case ResourceType.None:
          var ressourceType =
              (await calDavDataAccess.IsResourceCalender() ? ResourceType.Calendar : ResourceType.None) |
              (await cardDavDataAccess.IsResourceAddressBook() ? ResourceType.AddressBook : ResourceType.None);

          result = new TestResult (
              ressourceType,
              ressourceType.HasFlag (ResourceType.Calendar) ? await GetCalendarProperties (calDavDataAccess) : CalendarProperties.None,
              ressourceType.HasFlag (ResourceType.AddressBook) ? await GetAddressBookProperties (cardDavDataAccess) : AddressBookProperties.None);
              break;

        case ResourceType.Calendar:
          result = new TestResult (
              supposedRessourceType,
              CalendarProperties.CalendarAccessSupported |
              (await calDavDataAccess.IsWriteable() ? CalendarProperties.IsWriteable : CalendarProperties.None) |
              (await calDavDataAccess.DoesSupportCalendarQuery() ? CalendarProperties.SupportsCalendarQuery : CalendarProperties.None),
              AddressBookProperties.None);
          break;
        
        case ResourceType.AddressBook:
          result = new TestResult (
              supposedRessourceType,
              CalendarProperties.None,
              AddressBookProperties.AddressBookAccessSupported |
              (await cardDavDataAccess.IsWriteable() ? AddressBookProperties.IsWriteable : AddressBookProperties.None));
          break;
        
        default:
          throw new ArgumentOutOfRangeException ("supposedRessourceType");
      }

      return result;
    }

    private static async Task<CalendarProperties> GetCalendarProperties (CalDavDataAccess calDavDataAccess)
    {
      return
          (await calDavDataAccess.IsCalendarAccessSupported() ? CalendarProperties.CalendarAccessSupported : CalendarProperties.None) |
          (await calDavDataAccess.IsWriteable() ? CalendarProperties.IsWriteable : CalendarProperties.None) |
          (await calDavDataAccess.DoesSupportCalendarQuery() ? CalendarProperties.SupportsCalendarQuery : CalendarProperties.None);
    }

    private static async Task<AddressBookProperties> GetAddressBookProperties (CardDavDataAccess cardDavDataAccess)
    {
      return
          (await cardDavDataAccess.IsAddressBookAccessSupported() ? AddressBookProperties.AddressBookAccessSupported : AddressBookProperties.None) |
          (await cardDavDataAccess.IsWriteable() ? AddressBookProperties.IsWriteable : AddressBookProperties.None);
    }


    public static bool RequiresAutoDiscovery (Uri uri)
    {
      return uri.AbsolutePath == "/" || !uri.AbsolutePath.EndsWith ("/");
    }
  }
}