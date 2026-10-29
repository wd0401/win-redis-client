using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Presentation;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

/// <summary>快速移动分组的持久化与回滚验证。</summary>
[Collection("AppServicesSequential")]
public sealed class ConnectionMoveGroupTests
{
    [Fact]
    public async Task MoveToGroup_PersistsNewGroup()
    {
        var store = new MemoryConnectionStore();
        AppServices.ConnectionStore = store;
        try
        {
            var item = new ConnectionItemViewModel
            {
                Id = Guid.NewGuid(),
                Name = "local",
                Host = "127.0.0.1",
                Group = "旧分组",
            };
            var vm = new ConnectionListViewModel();
            vm.Connections.Add(item);

            var moved = await vm.MoveToGroupAsync(item, " 新分组 ");

            Assert.True(moved);
            Assert.Equal("新分组", item.Group);
            Assert.Equal("新分组", Assert.Single(store.Profiles).Group);
        }
        finally
        {
            AppServices.ConnectionStore = null;
        }
    }

    [Fact]
    public async Task MoveToGroup_EmptyNameMovesToUngrouped()
    {
        var store = new MemoryConnectionStore();
        AppServices.ConnectionStore = store;
        try
        {
            var item = new ConnectionItemViewModel
            {
                Id = Guid.NewGuid(),
                Name = "local",
                Host = "127.0.0.1",
                Group = "旧分组",
            };
            var vm = new ConnectionListViewModel();
            vm.Connections.Add(item);

            Assert.True(await vm.MoveToGroupAsync(item, " "));
            Assert.Equal(string.Empty, item.Group);
            Assert.Null(Assert.Single(store.Profiles).Group);
        }
        finally
        {
            AppServices.ConnectionStore = null;
        }
    }

    [Fact]
    public async Task MoveToGroup_SaveFailureRevertsGroup()
    {
        var store = new MemoryConnectionStore { FailOnSave = true };
        AppServices.ConnectionStore = store;
        try
        {
            var item = new ConnectionItemViewModel
            {
                Id = Guid.NewGuid(),
                Name = "local",
                Host = "127.0.0.1",
                Group = "原分组",
            };
            var vm = new ConnectionListViewModel();
            vm.Connections.Add(item);

            var moved = await vm.MoveToGroupAsync(item, "新分组");

            Assert.False(moved);
            Assert.Equal("原分组", item.Group);
        }
        finally
        {
            AppServices.ConnectionStore = null;
        }
    }

    private sealed class MemoryConnectionStore : IConnectionStore
    {
        public List<ConnectionProfile> Profiles { get; } = [];
        public bool FailOnSave { get; init; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>(Profiles.ToList());

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            if (FailOnSave) throw new InvalidOperationException("save failed");
            Profiles.RemoveAll(existing => existing.Id == profile.Id);
            Profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Profiles.RemoveAll(existing => existing.Id == id);
            return Task.CompletedTask;
        }
    }
}
