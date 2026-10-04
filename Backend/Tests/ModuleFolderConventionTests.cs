namespace CommonService.Tests;

public class ModuleFolderConventionTests
{
    private static readonly string[] Modules =
    [
        "Identity", "Customers", "Booking", "Payments", "Dispatch", "Workers",
        "Agencies", "Skills", "Ratings", "Disputes", "Payouts", "Admin"
    ];

    private static readonly string[] ModuleRoots =
    [
        "Application/Features", "WebAPI/Controllers", "Infrastructure/Modules", "Tests"
    ];

    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CommonService.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("CommonService.csproj not found above the test output directory");
    }

    public static IEnumerable<object[]> ModuleFolders() =>
        from root in ModuleRoots
        from module in Modules
        select new object[] { root, module };

    [Theory]
    [MemberData(nameof(ModuleFolders))]
    public void Module_folder_exists(string root, string module)
    {
        var path = Path.Combine(BackendRoot(), root, module);
        Assert.True(Directory.Exists(path), $"missing module folder: {root}/{module}");
    }
}
