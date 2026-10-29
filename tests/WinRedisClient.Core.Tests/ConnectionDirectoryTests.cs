using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Presentation;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

/// <summary>左侧连接目录（分组折叠/展开、预置分组快速选择）的行为验证。</summary>
[Collection("AppServicesSequential")]
public sealed class ConnectionDirectoryTests
{
    [Fact]
    public void DirectoryItems_GroupsConnectionsAndShowsHeaders()
    {
        var vm = CreateList(("a-prod", "生产"), ("b-prod", "生产"), ("c-local", null));

        var headers = vm.DirectoryItems.Where(d => d.IsGroupHeader).ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal(3, vm.DirectoryItems.Count - headers.Count); // 3 张连接卡片

        var prod = Assert.Single(headers, h => h.Group.Name == "生产");
        Assert.Equal(2, prod.Group.Count);
        Assert.All(vm.DirectoryItems.Where(d => !d.IsGroupHeader && d.Group == prod.Group),
            d => Assert.Contains("prod", d.Connection!.Name, StringComparison.Ordinal));

        // 未分组卡片仍然展示
        Assert.Contains(vm.DirectoryItems, d => !d.IsGroupHeader && d.Group.Name == "未分组");
    }

    [Fact]
    public async Task ToggleGroup_CollapsesAndPersistsState()
    {
        var settingsStore = new MemorySettingsStore();
        AppServices.SettingsStore = settingsStore;
        AppServices.Settings = AppSettings.Default;
        try
        {
            var vm = CreateList(("a-prod", "生产"), ("b-prod", "生产"), ("c-local", null));
            var header = vm.DirectoryItems.First(d => d.IsGroupHeader && d.Group.Name == "生产");
            var prodGroup = header.Group;

            vm.ToggleGroupCommand.Execute(prodGroup);

            Assert.False(prodGroup.IsExpanded);
            Assert.DoesNotContain(vm.DirectoryItems,
                d => !d.IsGroupHeader && d.Group == prodGroup);

            await Task.Delay(600); // 等待防抖持久化
            Assert.NotNull(settingsStore.Saved);
            Assert.Equal(["生产"], settingsStore.Saved.CollapsedConnectionGroups);

            // 重新加载后折叠状态恢复
            var vm2 = new ConnectionListViewModel();
            await vm2.LoadAsync();
            Assert.Contains(vm2.DirectoryItems,
                d => d.IsGroupHeader && d.Group.Name == "生产" && !d.Group.IsExpanded);
        }
        finally
        {
            AppServices.ConnectionStore = null;
            AppServices.SettingsStore = null;
            AppServices.Settings = AppSettings.Default;
        }
    }

    [Fact]
    public void Search_IgnoresCollapsedState()
    {
        var vm = CreateList(("a-prod", "生产"), ("b-prod", "生产"));
        var prodGroup = vm.DirectoryItems.First(d => d.IsGroupHeader).Group;
        vm.ToggleGroupCommand.Execute(prodGroup);
        Assert.DoesNotContain(vm.DirectoryItems, d => !d.IsGroupHeader && d.Group == prodGroup);

        vm.SearchText = "prod";

        Assert.Equal(3, vm.DirectoryItems.Count); // 标题 + 2 张卡片（搜索时忽略折叠）
        Assert.All(vm.DirectoryItems.Skip(1), d => Assert.False(d.IsGroupHeader));
    }

    [Fact]
    public async Task EmptyGroup_IsRemovedFromDirectory()
    {
        var vm = CreateList(("a-x", "临时"));
        Assert.Contains(vm.DirectoryItems, d => d.IsGroupHeader && d.Group.Name == "临时");

        var item = vm.Connections.Single();
        Assert.True(await vm.MoveToGroupAsync(item, null));

        Assert.DoesNotContain(vm.DirectoryItems, d => d.IsGroupHeader && d.Group.Name == "临时");
        Assert.Contains(vm.DirectoryItems, d => d.IsGroupHeader && d.Group.Name == "未分组");
    }

    [Fact]
    public void GetGroupOptions_ExistingGroupsFirstThenPresets()
    {
        var vm = CreateList(("a-z", "自定义组"), ("b-a", "另一个"));

        var options = vm.GetGroupOptions();

        Assert.Equal(["另一个", "自定义组", .. ConnectionGroupPresets.All], options);
        Assert.Equal(options, options.Distinct().ToList()); // 无重复
        Assert.Equal(["生产", "预发", "测试", "开发"], ConnectionGroupPresets.All);
    }

    /// <summary>通过真实 LoadAsync 路径构建列表，保证与运行时行为一致。</summary>
    private static ConnectionListViewModel CreateList(params (string Name, string? Group)[] items)
    {
        var store = new MemoryConnectionStore();
        foreach (var (name, group) in items)
        {
            store.Profiles.Add(new ConnectionProfile
            {
                Id = Guid.NewGuid(),
                Name = name,
                Host = "127.0.0.1",
                Port = 6379,
                Environment = EnvironmentTag.Development,
                Group = string.IsNullOrWhiteSpace(group) ? null : group,
            });
        }

        AppServices.ConnectionStore = store;
        var vm = new ConnectionListViewModel();
        vm.LoadAsync().GetAwaiter().GetResult();
        return vm;
    }

    private sealed class MemoryConnectionStore : IConnectionStore
    {
        public List<ConnectionProfile> Profiles { get; } = [];

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>(Profiles.ToList());

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
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

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public AppSettings? Saved { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Saved ?? AppSettings.Default);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
