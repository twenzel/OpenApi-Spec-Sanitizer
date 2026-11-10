#:sdk Cake.Sdk@6.3.0

#:package Cake.Sonar@6.0.1

InstallTools(
   "dotnet:?package=GitVersion.Tool&version=6.8.2",
   "dotnet:?package=dotnet-sonarscanner&version=12.1.0"
);

var target = Argument("target", "Default");
var sonarLoginToken = Argument("sonarLogin", EnvironmentVariable("SONAR_LOGIN") ?? "");
var nugetApiKey = Argument("nugetApiKey", EnvironmentVariable("NUGET_API_KEY") ?? "");

//////////////////////////////////////////////////////////////////////
//    Build Variables
/////////////////////////////////////////////////////////////////////
var solution = "OpenApiSpecSanitizer.slnx";
var outputDir = MakeAbsolute(Directory("./buildArtifacts/"));
var outputDirNuget = outputDir.Combine("NuGet");
var sonarProjectKey = "twenzel_OpenApi-Spec-Sanitizer";
var sonarUrl = "https://sonarcloud.io";
var sonarOrganization = "twenzel";

var outputDirTemp = outputDir.Combine("Temp");
var outputDirTests = outputDirTemp.Combine("Tests");

var codeCoverageResultFilePath = MakeAbsolute(outputDirTests).Combine("**/").CombineWithFilePath("coverage.opencover.xml");
var testResultsPath = MakeAbsolute(outputDirTests).CombineWithFilePath("*.trx");

var nugetPublishFeed = "https://api.nuget.org/v3/index.json";

var isLocalBuild = BuildSystem.IsLocalBuild;
var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("refs/heads/main", BuildSystem.GitHubActions.Environment.Workflow.Ref);
var isPullRequest = BuildSystem.GitHubActions.Environment.PullRequest.IsPullRequest;
var runSonar = !string.IsNullOrWhiteSpace(sonarLoginToken);

var gitHubEvent = EnvironmentVariable("GITHUB_EVENT_NAME");
var isReleaseCreation = string.Equals(gitHubEvent, "release");

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Setup(context =>
{
    Information($"Local build: {isLocalBuild}");
    Information($"Main branch: {isMainBranch}");
    Information($"Pull request: {isPullRequest}");
    Information($"Run sonar: {runSonar}");
    Information($"ref: {BuildSystem.GitHubActions.Environment.Workflow.Ref}");
    Information($"Is release creation: {isReleaseCreation}");
});

Task("Clean")
    .Description("Removes the output directory")
    .Does(() => {

        EnsureDirectoryDoesNotExist(outputDir, new DeleteDirectorySettings
        {
            Recursive = true,
            Force = true
        });
        CreateDirectory(outputDir);
    });

GitVersion versionInfo = null!;
Task("Version")
    .Description("Retrieves the current version from the git repository")
    .Does(() => {

        versionInfo = GitVersion(new GitVersionSettings
        {
            UpdateAssemblyInfo = false
        });

        Information("Version: " + versionInfo.FullSemVer);
    });

Task("Build")
    .IsDependentOn("Clean")
    .IsDependentOn("Version")
    .Does(() => {

        var msBuildSettings = new DotNetMSBuildSettings()
        {
            Version = versionInfo!.AssemblySemVer,
            InformationalVersion = versionInfo!.InformationalVersion,
            PackageVersion = versionInfo!.NuGetVersionV2
        }.WithProperty("PackageOutputPath", outputDirNuget.FullPath);

        var settings = new DotNetBuildSettings
        {
            Configuration = "Release",
            MSBuildSettings = msBuildSettings
        };

        DotNetBuild(solution, settings);
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
    {

        var settings = new DotNetTestSettings
        {
            Configuration = "Release",
            Loggers = new[] { "trx;" },
            ResultsDirectory = outputDirTests,
            Collectors = new[] { "XPlat Code Coverage" },
            ArgumentCustomization = a => a.Append("-- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover"),
            NoBuild = true
        };

        DotNetTest(solution, settings);
    });

Task("SonarBegin")
    .WithCriteria(runSonar)
    .Does(() => {
        SonarBegin(new SonarBeginSettings
        {
            Key = sonarProjectKey,
            Url = sonarUrl,
            Organization = sonarOrganization,
            Token = sonarLoginToken,
            UseCoreClr = true,
            VsTestReportsPath = testResultsPath.ToString(),
            OpenCoverReportsPath = codeCoverageResultFilePath.ToString()
        });
    });

Task("SonarEnd")
    .WithCriteria(runSonar)
    .Does(() => {
        SonarEnd(new SonarEndSettings
        {
            Token = sonarLoginToken
        });
    });

Task("Publish")
    .WithCriteria(isReleaseCreation)
    .IsDependentOn("Test")
    .IsDependentOn("Version")
    .Description("Pushes the created NuGet packages to nuget.org")
    .Does(() => {
        Information($"Upload packages from {outputDirNuget.FullPath}");

        // Get the paths to the packages ordered by the file names in order to get the nupkg first.
        var packages = GetFiles(outputDirNuget.CombineWithFilePath("*.*nupkg").ToString()).OrderBy(x => x.FullPath).ToArray();

        if (packages.Length == 0)
        {
            Error("No packages found to upload");
            return;
        }

        foreach (var package in packages)
        {
            Information($"Found package to upload: {package.FullPath}");
            DotNetNuGetPush(package, new DotNetNuGetPushSettings
            {
                Source = nugetPublishFeed,
                ApiKey = nugetApiKey,
                SkipDuplicate = true
            });
        }
    });

Task("Default")
    .IsDependentOn("SonarBegin")
    .IsDependentOn("Test")
    .IsDependentOn("SonarEnd")
    .IsDependentOn("Publish");

RunTarget(target);