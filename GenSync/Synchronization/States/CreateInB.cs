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
using System.Reflection;
using System.Threading.Tasks;
using GenSync.EntityRelationManagement;
using GenSync.Logging;
using log4net;

namespace GenSync.Synchronization.States
{
  internal class CreateInB<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity> :
      StateBase<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity>
  {
    // ReSharper disable once StaticFieldInGenericType
    private static readonly ILog s_logger = LogManager.GetLogger (MethodInfo.GetCurrentMethod().DeclaringType);

    private readonly TAtypeEntityId _aId;
    private readonly TAtypeEntityVersion _aVersion;
    private TAtypeEntity _aEntity;


    public CreateInB (EntitySyncStateEnvironment<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity> environment, TAtypeEntityId aId, TAtypeEntityVersion aVersion)
        : base (environment)
    {
      _aId = aId;
      _aVersion = aVersion;
    }

    public override void AddRequiredEntitiesToLoad (Func<TAtypeEntityId, bool> a, Func<TBtypeEntityId, bool> b)
    {
      a (_aId);
    }

    public override IEntitySyncState<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity> FetchRequiredEntities (IReadOnlyDictionary<TAtypeEntityId, TAtypeEntity> aEntities, IReadOnlyDictionary<TBtypeEntityId, TBtypeEntity> bEntites)
    {
      if (!aEntities.TryGetValue (_aId, out _aEntity))
      {
        // Just an info, because an add will be retried on next synchronization
        s_logger.InfoFormat ("Could not fetch entity '{0}'. Discarding operation.", _aId);
        return Discard();
      }

      return this;
    }

    public override IEntitySyncState<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity> Resolve ()
    {
      return this;
    }

    public override async Task<IEntitySyncState<TAtypeEntityId, TAtypeEntityVersion, TAtypeEntity, TBtypeEntityId, TBtypeEntityVersion, TBtypeEntity>> PerformSyncActionNoThrow (
        IEntitySynchronizationLogger logger)
    {
      try
      {
        logger.SetAId (_aId);
        var newB = await _environment.BRepository.Create (b => _environment.Mapper.Map1To2 (_aEntity, b, logger));
        logger.SetBId (newB.Id);
        return CreateDoNothing (_aId, _aVersion, newB.Id, newB.Version);
      }
      catch (Exception x)
      {
        logger.LogAbortedDueToError (x);
        LogException (x);
        return Discard();
      }
    }

    public override void AddNewRelationNoThrow (Action<IEntityRelationData<TAtypeEntityId, TAtypeEntityVersion, TBtypeEntityId, TBtypeEntityVersion>> addAction)
    {
      s_logger.Error ("This state should have been left via PerformSyncActionNoThrow!");
    }

    public override void Dispose ()
    {
      _aEntity = default(TAtypeEntity);
    }
  }
}