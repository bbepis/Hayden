using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Npm;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Package);

	//[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
	//readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

	readonly string version = "0.9.0";

	AbsolutePath BuildOutputDirectory => RootDirectory / "build-output";

	readonly string[] publishRuntimes =
	{
		"win-x64",
		"linux-x64",
		//"any"
	};

	Target NpmInstall => _ => _
		.Executes(() =>
		{
			NpmTasks.NpmInstall(x => x
				.SetProcessWorkingDirectory("./Hayden.Frontend"));
		});

	Target NpmPublish => _ => _
		.DependsOn(NpmInstall)
		.Executes(() =>
		{
			NpmTasks.NpmRun(x => x
				.SetProcessWorkingDirectory("./Hayden.Frontend")
				.SetCommand("publish"));
		});

	Target Clean => _ => _
		.Before(Restore)
		.Executes(() =>
		{
			DotNetTasks.DotNetClean(x => x
				.SetProject("./Hayden.sln"));
		});

	Target Restore => _ => _
		.Executes(() =>
		{
			// no point, --no-restore is broken when using RIDs

			//DotNetTasks.DotNetRestore(x => x
			//	.SetProjectFile("./Hayden.WebServer/Hayden.WebServer.csproj"));

			//DotNetTasks.DotNetRestore(x => x
			//	.SetProjectFile("./Hayden/Hayden.csproj"));
		});
	
	void DoPublish(string prefix, string projectPath, bool package)
	{
		foreach (string runtime in publishRuntimes)
		{
			var runtimeName = runtime != "any"
				? runtime
				: "portable";

			Serilog.Log.Information($"Publishing {prefix} {runtimeName}");

			var outputFolder = BuildOutputDirectory / prefix / runtimeName;
			var outputZip = BuildOutputDirectory / $"{prefix}-{version}-{runtimeName}.zip";

			EnsureCleanDirectory(outputFolder);
			DotNetTasks.DotNetPublish(x => x
				.SetProject(projectPath)
				.SetOutput(outputFolder)
				.SetConfiguration("Release")
				.SetRuntime(runtime != "any" ? runtime : null)
				.DisableSelfContained());

			if (package)
			{
				CompressionTasks.CompressZip(outputFolder, outputZip);
				DeleteDirectory(outputFolder);
			}
		}
	}

	Target PackageCli => _ => _
		.DependsOn(Restore)
		.Executes(() =>
		{
			EnsureExistingDirectory(BuildOutputDirectory);
			DoPublish("hayden-cli", "./Hayden/Hayden.csproj", true);
		});

	Target PackageServer => _ => _
		.DependsOn(Restore)
		.DependsOn(NpmPublish)
		.Executes(() =>
		{
			EnsureExistingDirectory(BuildOutputDirectory);
			DoPublish("hayden-server", "./Hayden.WebServer/Hayden.WebServer.csproj", true);
		});

	Target PrePackage => _ => _
		.Before(Package)
		.Before(PackageCli)
		.Before(PackageServer)
		.Executes(() =>
		{
			EnsureCleanDirectory(BuildOutputDirectory);
		});

	Target Package => _ => _
		.DependsOn(PrePackage)
		.DependsOn(PackageCli)
		.DependsOn(PackageServer)
		.Executes(() =>
		{
		});
}
