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
using System.Reflection;
using System.Threading.Tasks;
using GenSync.EntityRelationManagement;
using GenSync.Logging;
using log4net;

namespace GenSync.Synchronization.States
{
  internal class UpdateBToA<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity>
      : UpdateBase<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity>
  {
    // ReSharper disable once StaticFieldInGenericType
    private static readonly ILog s_logger = LogManager.GetLogger (MethodInfo.GetCurrentMethod().DeclaringType);
    private readonly TBtypeEntityVersion _newBVersion;
    private readonly TAtypeEntityVersion _currentAVersion;

    public UpdateBToA (
        EntitySyncStateEnvironment<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity> environment,
        IEntityRelationData<TAtypeEntityId, TAtypeEntityVersion, TBtypeEntityId, TBtypeEntityVersion> knownData,
        TBtypeEntityVersion newBVersion,
        TAtypeEntityVersion currentAVersion)
        : base (environment, knownData)
    {
      _newBVersion = newBVersion;
      _currentAVersion = currentAVersion;
    }

    public override async Task<IEntitySyncState<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity>> PerformSyncActionNoThrow (
        IEntitySynchronizationLogger logger)
    {
      try
      {
        logger.SetAId (_knownData.AtypeId);
        logger.SetBId (_knownData.BtypeId);
        var newA = await _environment.ARepository.Update (
            _knownData.AtypeId,
            _currentAVersion,
            _aEntity,
            a => _environment.Mapper.Map2To1 (_bEntity, a, logger));
        logger.SetAId (newA.Id);
        return CreateDoNothing (newA.Id, newA.Version, _knownData.BtypeId, _newBVersion);
      }
      catch (Exception x)
      {
        logger.LogAbortedDueToError (x);
        LogException (x);
        return CreateDoNothing();
      }
    }


    public override void AddNewRelationNoThrow (Action<IEntityRelationData<TAtypeEntityId, TAtypeEntityVersion, TBtypeEntityId, TBtypeEntityVersion>> addAction)
    {
      s_logger.Error ("This state should have been left via PerformSyncActionNoThrow!");
    }
  }
}