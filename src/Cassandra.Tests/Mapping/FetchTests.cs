﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra.Mapping;
using Cassandra.Tasks;
using Cassandra.Tests.Mapping.FluentMappings;
using Cassandra.Tests.Mapping.Pocos;
using Cassandra.Tests.Mapping.TestData;
using Moq;
using NUnit.Framework;

namespace Cassandra.Tests.Mapping
{
    public class FetchTests : MappingTestBase
    {
        [Test]
        public void FetchAsync_Pocos_WithCql_Empty()
        {
            var rowset = new RowSet();
            var mappingClient = GetMappingClient(rowset);
            var userTask = mappingClient.FetchAsync<PlainUser>("SELECT * FROM users");
            var users = userTask.Result;
            Assert.NotNull(users);
            Assert.AreEqual(0, users.Count());
        }

        [Test]
        public void FetchAsync_Pocos_WithCql_Single_Column_Maps()
        {
            //just the userid
            var usersExpected = TestDataHelper.GetUserList().Select(u => new PlainUser { UserId = u.UserId} ).ToList();
            var rowset = TestDataHelper.GetUsersRowSet(usersExpected);
            var mappingClient = GetMappingClient(rowset);
            var userTask = mappingClient.FetchAsync<PlainUser>("SELECT * FROM users");
            var users = userTask.Result;
            Assert.NotNull(users);
            CollectionAssert.AreEqual(usersExpected, users, new TestHelper.PropertyComparer());
        }

        [Test]
        public void FetchAsync_Pocos_Prepares_Just_Once()
        {
            const int times = 100;
            var users = TestDataHelper.GetUserList();
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.ExecuteAsync(It.IsAny<BoundStatement>()))
                .Returns(() => TaskHelper.ToTask(TestDataHelper.GetUsersRowSet(users)))
                .Verifiable();
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(TaskHelper.ToTask(GetPrepared()))
                .Verifiable();
            var mappingClient = GetMappingClient(sessionMock);
            var taskList = new List<Task<IEnumerable<PlainUser>>>();
            for (var i = 0; i < times; i++)
            {
                var t = mappingClient.FetchAsync<PlainUser>("SELECT * FROM users");
                taskList.Add(t);
            }

            Task.WaitAll(taskList.Select(t => (Task)t).ToArray(), 5000);
            Assert.True(taskList.All(t => t.Result.Count() == 10));
            sessionMock.Verify();
            //Prepare should be called just once
            sessionMock
                .Verify(s => s.PrepareAsync(It.IsAny<string>()), Times.Once());
            //ExecuteAsync should be called the exact number of times
            sessionMock
                .Verify(s => s.ExecuteAsync(It.IsAny<BoundStatement>()), Times.Exactly(times));
            sessionMock.Verify();
        }

        [Test]
        public void Fetch_Throws_ExecuteAsync_Exception()
        {
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.ExecuteAsync(It.IsAny<BoundStatement>()))
                .Returns(() => TaskHelper.FromException<RowSet>(new InvalidQueryException("Mocked Exception")))
                .Verifiable();
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(TaskHelper.ToTask(GetPrepared()))
                .Verifiable();
            var mappingClient = GetMappingClient(sessionMock);
            var ex = Assert.Throws<InvalidQueryException>(() => mappingClient.Fetch<PlainUser>("SELECT WILL FAIL FOR INVALID"));
            Assert.AreEqual(ex.Message, "Mocked Exception");
            sessionMock.Verify();
        }

        [Test]
        public void Fetch_Throws_PrepareAsync_Exception()
        {
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(() => TaskHelper.FromException<PreparedStatement>(new SyntaxError("Mocked Exception 2")))
                .Verifiable();
            var mappingClient = GetMappingClient(sessionMock);
            var ex = Assert.Throws<SyntaxError>(() => mappingClient.Fetch<PlainUser>("SELECT WILL FAIL FOR SYNTAX"));
            Assert.AreEqual(ex.Message, "Mocked Exception 2");
            sessionMock.Verify();
        }

        [Test]
        public void FetchAsync_Pocos_WithCqlAndOptions()
        {
            var usersExpected = TestDataHelper.GetUserList();
            var rowset = TestDataHelper.GetUsersRowSet(usersExpected);
            var mappingClient = GetMappingClient(rowset);
            var users = mappingClient.FetchAsync<PlainUser>(Cql.New("SELECT * FROM users").WithOptions(opt => opt.SetConsistencyLevel(ConsistencyLevel.Quorum))).Result;
            CollectionAssert.AreEqual(users, usersExpected, new TestHelper.PropertyComparer());
        }

        [Test]
        public void Fetch_Fluent_Mapping()
        {
            var usersOriginal = TestDataHelper.GetUserList();
            var rowset = TestDataHelper.GetUsersRowSet(usersOriginal);
            var mappingClient = GetMappingClient(rowset);
            var users = mappingClient.Fetch<FluentUser>("SELECT * FROM users").ToList();
            Assert.AreEqual(usersOriginal.Count, users.Count);
            for (var i = 0; i < users.Count; i++)
            {
                var expected = usersOriginal[i];
                var user = users[i];
                Assert.AreEqual(expected.UserId, user.Id);
                Assert.AreEqual(expected.Age, user.Age);
                Assert.AreEqual(expected.ChildrenAges.ToDictionary(t => t.Key, t => t.Value), user.ChildrenAges.ToDictionary(t => t.Key, t => t.Value));
                Assert.AreEqual(expected.HairColor, user.HairColor);
            }
        }

        [Test]
        public void Fetch_Invalid_Type_Conversion_Throws()
        {
            var usersOriginal = TestDataHelper.GetUserList();
            var rowset = TestDataHelper.GetUsersRowSet(usersOriginal);
            var mappingClient = GetMappingClient(rowset);
            var ex = Assert.Throws<InvalidTypeException>(() => mappingClient.Fetch<UserDifferentPropTypes>("SELECT * FROM users"));
            //Message contains column name
            StringAssert.Contains("age", ex.Message.ToLower());
            //Source type
            StringAssert.Contains("int", ex.Message.ToLower());
            //Target type
            StringAssert.Contains("Dictionary", ex.Message);
        }

        [Test]
        public void Fetch_Invalid_Constructor_Throws()
        {
            var usersOriginal = TestDataHelper.GetUserList();
            var rowset = TestDataHelper.GetUsersRowSet(usersOriginal);
            var mappingClient = GetMappingClient(rowset);
            var ex = Assert.Throws<ArgumentException>(() => mappingClient.Fetch<SomeClassWithNoDefaultConstructor>("SELECT * FROM users"));
            StringAssert.Contains("constructor", ex.Message);
        }

        private class SomeClassWithNoDefaultConstructor
        {
            public SomeClassWithNoDefaultConstructor(string w) { }
        }

        [Test]
        public void Fetch_Lazily_Pages()
        {
            const int pageSize = 10;
            const int totalPages = 4;
            var rs = TestDataHelper.CreateMultipleValuesRowSet(new[] {"title", "artist"}, new[] {"Once in a Livetime", "Dream Theater"}, pageSize);
            rs.PagingState = new byte[] {1};
            rs.FetchNextPage = state =>
            {
                var pageNumber = state[0];
                pageNumber++;
                var nextRs = TestDataHelper.CreateMultipleValuesRowSet(new[] {"title", "artist"}, new[] {"Once in a Livetime " + pageNumber, "Dream Theater"}, pageSize);
                if (pageNumber < totalPages)
                {
                    nextRs.PagingState = new[] { pageNumber };
                }
                return nextRs;
            };
            var mappingClient = GetMappingClient(rs);
            var songs = mappingClient.Fetch<Song>("SELECT * FROM songs");
            //Page to all the values
            var allSongs = songs.ToList();
            Assert.AreEqual(pageSize * totalPages, allSongs.Count);
        }

        [Test]
        public void FetchPageAsync_Pocos_Uses_Defaults()
        {
            var rs = TestDataHelper.GetUsersRowSet(TestDataHelper.GetUserList());
            rs.AutoPage = false;
            rs.PagingState = new byte[] { 1, 2, 3 };
            BoundStatement stmt = null;
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.ExecuteAsync(It.IsAny<BoundStatement>()))
                .Callback<IStatement>(s => stmt = (BoundStatement)s)
                .Returns(() => TestHelper.DelayedTask(rs))
                .Verifiable();
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(TaskHelper.ToTask(GetPrepared()))
                .Verifiable();
            var mappingClient = GetMappingClient(sessionMock);
            IPage<PlainUser> page = mappingClient.FetchPageAsync<PlainUser>(Cql.New("SELECT * FROM users")).Result;
            Assert.Null(page.CurrentPagingState);
            Assert.NotNull(page.PagingState);
            Assert.AreEqual(rs.PagingState, page.PagingState);
            sessionMock.Verify();
            Assert.False(stmt.AutoPage);
            Assert.AreEqual(0, stmt.PageSize);
        }

        [Test]
        public void FetchPageAsync_Pocos_WithCqlAndOptions()
        {
            const int pageSize = 10;
            var usersExpected = TestDataHelper.GetUserList(pageSize);
            var rs = TestDataHelper.GetUsersRowSet(usersExpected);
            rs.AutoPage = false;
            rs.PagingState = new byte[] {1, 2, 3};
            BoundStatement stmt = null;
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.ExecuteAsync(It.IsAny<BoundStatement>()))
                .Callback<IStatement>(s => stmt = (BoundStatement)s)
                .Returns(() => TestHelper.DelayedTask(rs, 50))
                .Verifiable();
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(TaskHelper.ToTask(GetPrepared()))
                .Verifiable();
            var mappingClient = GetMappingClient(sessionMock);
            IPage<PlainUser> page = mappingClient.FetchPageAsync<PlainUser>(Cql.New("SELECT * FROM users").WithOptions(opt => opt.SetPageSize(pageSize))).Result;
            Assert.Null(page.CurrentPagingState);
            Assert.NotNull(page.PagingState);
            Assert.AreEqual(rs.PagingState, page.PagingState);
            CollectionAssert.AreEqual(page, usersExpected, new TestHelper.PropertyComparer());
            sessionMock.Verify();
            Assert.False(stmt.AutoPage);
            Assert.AreEqual(pageSize, stmt.PageSize);
        }

        [Test]
        public void Fetch_Sets_Consistency()
        {
            ConsistencyLevel? consistency = null;
            ConsistencyLevel? serialConsistency = null;
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.ExecuteAsync(It.IsAny<BoundStatement>()))
                .Callback<IStatement>(b =>
                {
                    consistency = b.ConsistencyLevel;
                    serialConsistency = b.SerialConsistencyLevel;
                })
                .Returns(() => TaskHelper.ToTask(TestDataHelper.GetUsersRowSet(TestDataHelper.GetUserList())))
                .Verifiable();
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(TaskHelper.ToTask(GetPrepared()))
                .Verifiable();
            var mapper = GetMappingClient(sessionMock);
            mapper.Fetch<PlainUser>(new Cql("SELECT").WithOptions(o => o.SetConsistencyLevel(ConsistencyLevel.EachQuorum).SetSerialConsistencyLevel(ConsistencyLevel.Serial)));
            Assert.AreEqual(ConsistencyLevel.EachQuorum, consistency);
            Assert.AreEqual(ConsistencyLevel.Serial, serialConsistency);
        }

        [Test]
        public void Fetch_Maps_NullableDateTime_Test()
        {
            var rs = new RowSet
            {
                Columns = new[]
                {
                    new CqlColumn {Name = "id", TypeCode = ColumnTypeCode.Uuid, Type = typeof (Guid), Index = 0},
                    new CqlColumn {Name = "title", TypeCode = ColumnTypeCode.Text, Type = typeof (string), Index = 1},
                    new CqlColumn {Name = "releasedate", TypeCode = ColumnTypeCode.Timestamp, Type = typeof (DateTimeOffset), Index = 2}
                }
            };
            var values = new object[] { Guid.NewGuid(), "Come Away with Me", DateTimeOffset.Parse("2002-01-01 +0")}
                .Select(v => TypeCodec.Encode(2, v));
            var row = new Row(2, values.ToArray(), rs.Columns, rs.Columns.ToDictionary(c => c.Name, c => c.Index));
            rs.AddRow(row);
            var sessionMock = new Mock<ISession>(MockBehavior.Strict);
            sessionMock
                .Setup(s => s.ExecuteAsync(It.IsAny<BoundStatement>()))
                .Returns(TestHelper.DelayedTask(rs, 100))
                .Verifiable();
            sessionMock
                .Setup(s => s.PrepareAsync(It.IsAny<string>()))
                .Returns(TaskHelper.ToTask(GetPrepared()))
                .Verifiable();
            var mapper = GetMappingClient(sessionMock);
            var song = mapper.Fetch<Song2>(new Cql("SELECT * FROM songs")).First();
            Assert.AreEqual("Come Away with Me", song.Title);
            Assert.AreEqual(DateTimeOffset.Parse("2002-01-01 +0").DateTime, song.ReleaseDate);
        }
    }
}
