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
using GenSync.EntityMapping;
using GenSync.EntityRelationManagement;
using GenSync.EntityRepositories;
using GenSync.InitialEntityMatching;
using GenSync.ProgressReport;
using GenSync.Synchronization;
using GenSync.Synchronization.StateCreationStrategies;
using Rhino.Mocks;

namespace GenSync.UnitTests.Synchronization.Stubs
{
  internal class SynchronizerBuilder
  {
    public SynchronizerBuilder ()
    {
      AtypeRepository = MockRepository.GenerateMock<IEntityRepository<string, string, string>>();
      BtypeRepository = MockRepository.GenerateMock<IEntityRepository<string, string, string>>();
      EntityMapper = MockRepository.GenerateMock<IEntityMapper<string, string>>();
      EntityRelationDataAccess = MockRepository.GenerateMock<IEntityRelationDataAccess<string, string, string, string>>();
      EntityRelationDataFactory = MockRepository.GenerateMock<IEntityRelationDataFactory<string, string, string, string>>();
      InitialEntityMatcher = MockRepository.GenerateMock<IInitialEntityMatcher<string, string, string, string, string, string>>();
      InitialSyncStateCreationStrategy = MockRepository.GenerateMock<IInitialSyncStateCreationStrategy<string, string, string, string, string, string>>();
    }

    public Synchronizer<string, string, string, string, string, string> Build ()
    {
      return new Synchronizer<string, string, string, string, string, string> (
          AtypeRepository,
          BtypeRepository,
          InitialSyncStateCreationStrategy,
          EntityRelationDataAccess,
          EntityRelationDataFactory,
          InitialEntityMatcher,
          AtypeIdComparer,
          BtypeIdComparer,
          new NullTotalProgressFactory(),
          MockRepository.GenerateMock<IExceptionLogger>());
    }

    public IEntityRelationDataAccess<string, string, string, string> EntityRelationDataAccess { get; set; }

    public IEqualityComparer<string> BtypeIdComparer { get; set; }

    public IEqualityComparer<string> AtypeIdComparer { get; set; }

    public IInitialSyncStateCreationStrategy<string, string, string, string, string, string> InitialSyncStateCreationStrategy { get; set; }

    public IInitialEntityMatcher<string, string, string, string, string, string> InitialEntityMatcher { get; set; }

    public IEntityRelationDataFactory<string, string, string, string> EntityRelationDataFactory { get; set; }

    public IEntityMapper<string, string> EntityMapper { get; set; }

    public IEntityRepository<string, string, string> BtypeRepository { get; set; }

    public IEntityRepository<string, string, string> AtypeRepository { get; set; }
  }
}