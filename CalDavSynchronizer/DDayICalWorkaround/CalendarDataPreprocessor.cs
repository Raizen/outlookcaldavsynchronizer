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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using log4net;

namespace CalDavSynchronizer.DDayICalWorkaround
{
  public static class CalendarDataPreprocessor
  {
    private static readonly ILog s_logger = LogManager.GetLogger (MethodInfo.GetCurrentMethod().DeclaringType);

    public static void FixTimeZoneDSTRRules (TimeZoneInfo tz, DDay.iCal.iCalTimeZone iCalTz)
    {
      var adjustments = tz.GetAdjustmentRules();
      foreach (var tziItems in iCalTz.TimeZoneInfos)
      {
        var matchingAdj = adjustments.FirstOrDefault (a => (a.DateStart.Year == tziItems.Start.Year)) ?? adjustments.FirstOrDefault();
        if (matchingAdj != null && matchingAdj.DateEnd.Year != 9999)
        {
          if (!(tziItems.Name.Equals ("STANDARD") && matchingAdj == adjustments.Last()))
          {
            tziItems.RecurrenceRules[0].Until = DateTime.SpecifyKind (matchingAdj.DateEnd.Date.AddDays (1).Subtract (tz.BaseUtcOffset), DateTimeKind.Utc);
          }
        }
      }
    }

    public static string FixInvalidDTSTARTInTimeZoneNoThrow (string iCalendarData)
    {
      if (string.IsNullOrEmpty (iCalendarData))
        return iCalendarData;

      try
      {
        string fixedIcalendarData = iCalendarData;

        for (
            var timeZoneMatch = Regex.Match (iCalendarData, "BEGIN:VTIMEZONE\r?\n(.|\n)*?END:VTIMEZONE\r?\n", RegexOptions.RightToLeft);
            timeZoneMatch.Success;
            timeZoneMatch = timeZoneMatch.NextMatch())
        {
          var fixedTimeZone = timeZoneMatch.Value.Replace ("DTSTART:00010101", "DTSTART:19700101");
          fixedIcalendarData = fixedIcalendarData.Replace (timeZoneMatch.Value, fixedTimeZone);
        }
        return fixedIcalendarData;
      }
      catch (Exception x)
      {
        s_logger.Error ("Could not process calender data. Using original data", x);
        return iCalendarData;
      }
    }

    public static string FixTimeZoneComponentOrderNoThrow (string iCalendarData)
    {
      if (string.IsNullOrEmpty (iCalendarData))
        return iCalendarData;

      try
      {
        var newCalendarData = iCalendarData;

        for (
            var timeZoneMatch = Regex.Match (iCalendarData, "BEGIN:VTIMEZONE\r?\n(.|\n)*?END:VTIMEZONE\r?\n", RegexOptions.RightToLeft);
            timeZoneMatch.Success;
            timeZoneMatch = timeZoneMatch.NextMatch())
        {
          var sections = new List<Tuple<Match, DateTime>>();

          for (
              var sectionMatch = Regex.Match (timeZoneMatch.Value, "BEGIN:STANDARD\r?\n(.|\n)*?END:STANDARD\r?\n", RegexOptions.RightToLeft);
              sectionMatch.Success;
              sectionMatch = sectionMatch.NextMatch())
          {
            var startMatch = Regex.Match (sectionMatch.Value, "DTSTART:(.*?)\r?\n");
            if (startMatch.Success)
            {
              DateTime date;
              if (DateTime.TryParseExact (startMatch.Groups[1].Value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
              {
                sections.Add (Tuple.Create (sectionMatch, date));
              }
            }
          }

          for (
              var sectionMatch = Regex.Match (timeZoneMatch.Value, "BEGIN:DAYLIGHT\r?\n(.|\n)*?END:DAYLIGHT\r?\n", RegexOptions.RightToLeft);
              sectionMatch.Success;
              sectionMatch = sectionMatch.NextMatch())
          {
            var startMatch = Regex.Match (sectionMatch.Value, "DTSTART:(.*?)\r?\n");
            if (startMatch.Success)
            {
              DateTime date;
              if (DateTime.TryParseExact (startMatch.Groups[1].Value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
              {
                sections.Add (Tuple.Create (sectionMatch, date));
              }
            }
          }

          if (sections.Count > 0)
          {
            Match firstSection = null;

            var newTimeZoneData = timeZoneMatch.Value;

            foreach (var section in sections.OrderByDescending (s => s.Item1.Index))
            {
              newTimeZoneData = newTimeZoneData.Remove (section.Item1.Index, section.Item1.Length);
              firstSection = section.Item1;
            }

            foreach (var section in sections.OrderByDescending (s => s.Item2))
            {
              newTimeZoneData = newTimeZoneData.Insert (firstSection.Index, section.Item1.Value);
            }

            newCalendarData = newCalendarData.Remove (timeZoneMatch.Index, timeZoneMatch.Length);
            newCalendarData = newCalendarData.Insert (timeZoneMatch.Index, newTimeZoneData);
          }
        }

        return newCalendarData;
      }
      catch (Exception x)
      {
        s_logger.Error ("Could not process calender data. Using original data", x);
        return iCalendarData;
      }
    }

     public static string NormalizeLineBreaks (string iCalendarData)
     {
       // Certain iCal providers like Open-Xchange deliver their data with unexpected linebreaks
       // which causes DDay.iCal to fail. This can be fixed by normalizing the unexpected \r\r\n to \r\n
       return iCalendarData.Replace("\r\r\n", "\r\n");
     }
  }
}