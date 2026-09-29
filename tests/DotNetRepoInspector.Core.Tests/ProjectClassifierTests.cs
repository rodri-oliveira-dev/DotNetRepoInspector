using DotNetRepoInspector.Core.Classification;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class ProjectClassifierTests
{
    private static readonly string[] WebSdk = [DeterministicProjectClassifier.WebSdk];
    private static readonly string[] BlazorWebAssemblySdk =
        [DeterministicProjectClassifier.BlazorWebAssemblySdk];
    private static readonly string[] WorkerSdk = [DeterministicProjectClassifier.WorkerSdk];
    private static readonly string[] WebAndWorkerSdks =
    [
        DeterministicProjectClassifier.WebSdk,
        DeterministicProjectClassifier.WorkerSdk
    ];

    private readonly DeterministicProjectClassifier _classifier = new();

    [Fact]
    public void Classify_TestOverridesExecutableAndSpecializedSdkSignals()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebAndWorkerSdks,
            "Exe",
            true));

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:IsTestProject=true", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_TestOverridesWorkerPropertyAndServiceLifetimeSignals()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Exe",
            true)
        {
            UsingMicrosoftNETSdkWorker = true,
            PackageReferences =
            [
                DeterministicProjectClassifier.MicrosoftExtensionsHostingSystemdPackage
            ]
        });

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:IsTestProject=true", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_MtpApplicationOverridesExplicitVstestFalseAndWorkloadSignals()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebAndWorkerSdks,
            "Exe",
            false)
        {
            IsTestingPlatformApplication = true
        });

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            "property:IsTestingPlatformApplication=true",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_MSTestSdkIsAHighConfidenceTestSignal()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            [DeterministicProjectClassifier.MSTestSdk],
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            "sdk:MSTest.Sdk",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_MicrosoftNetTestSdkPackageIsFallbackWhenIsTestProjectIsMissing()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Library",
            null)
        {
            PackageReferences = [DeterministicProjectClassifier.MicrosoftNetTestSdkPackage]
        });

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal(
            "package:Microsoft.NET.Test.Sdk",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_MicrosoftNetTestSdkPackageDoesNotOverrideExplicitFalse()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Library",
            false)
        {
            PackageReferences = [DeterministicProjectClassifier.MicrosoftNetTestSdkPackage]
        });

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:OutputType=Library", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_WebSdkIsRecognizedBeforeOutputType()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.OpenApi")]
    [InlineData("Swashbuckle.AspNetCore")]
    public void Classify_WebApiPackageHintsDoNotInferSubtype(string packageReference)
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false)
        {
            PackageReferences = [packageReference]
        });

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation")]
    [InlineData("Microsoft.AspNetCore.Mvc.NewtonsoftJson")]
    public void Classify_MvcPackageHintsDoNotInferSubtype(string packageReference)
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false)
        {
            PackageReferences = [packageReference]
        });

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_AzureFunctionsSdkIsHighConfidenceIsolatedWorkerSubtype()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            [DeterministicProjectClassifier.AzureFunctionsSdk],
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            ProjectClassificationSubtypes.AzureFunctionsIsolated,
            classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.AzureFunctionsSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_LegacyAzureFunctionsIsolatedShapeIsHighConfidenceWorkerSubtype()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            ["Microsoft.NET.Sdk"],
            "Exe",
            false)
        {
            AzureFunctionsVersion = "v4",
            PackageReferences =
            [
                DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerPackage,
                DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerSdkPackage
            ]
        });

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            ProjectClassificationSubtypes.AzureFunctionsIsolated,
            classification.Subtype);
        Assert.Equal(
            [
                "property:AzureFunctionsVersion=v4",
                $"package:{DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerPackage}",
                $"package:{DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerSdkPackage}"
            ],
            classification.Signals);
    }

    [Fact]
    public void Classify_AzureFunctionsInProcessShapeIsHighConfidenceLibrarySubtype()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            ["Microsoft.NET.Sdk"],
            "Library",
            false)
        {
            AzureFunctionsVersion = "v4",
            PackageReferences =
            [
                DeterministicProjectClassifier.MicrosoftNetSdkFunctionsPackage
            ]
        });

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            ProjectClassificationSubtypes.AzureFunctionsInProcess,
            classification.Subtype);
        Assert.Equal(
            [
                "property:AzureFunctionsVersion=v4",
                $"package:{DeterministicProjectClassifier.MicrosoftNetSdkFunctionsPackage}"
            ],
            classification.Signals);
    }

    [Theory]
    [InlineData(DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerPackage)]
    [InlineData(DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerSdkPackage)]
    [InlineData(DeterministicProjectClassifier.MicrosoftNetSdkFunctionsPackage)]
    public void Classify_AzureFunctionsPackageAloneDoesNotInferSubtype(string packageReference)
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            ["Microsoft.NET.Sdk"],
            "Library",
            false)
        {
            PackageReferences = [packageReference]
        });

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            "property:OutputType=Library",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_AzureFunctionsVersionAloneDoesNotInferSubtype()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            ["Microsoft.NET.Sdk"],
            "Exe",
            false)
        {
            AzureFunctionsVersion = "v4"
        });

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            "property:OutputType=Exe",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_AzureFunctionsMixedModelsReturnUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            ["Microsoft.NET.Sdk"],
            "Exe",
            false)
        {
            AzureFunctionsVersion = "v4",
            PackageReferences =
            [
                DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerPackage,
                DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerSdkPackage,
                DeterministicProjectClassifier.MicrosoftNetSdkFunctionsPackage
            ]
        });

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Contains("conflict:azure-functions-model", classification.Signals);
    }

    [Fact]
    public void Classify_TestOverridesAzureFunctionsSdk()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            [DeterministicProjectClassifier.AzureFunctionsSdk],
            "Exe",
            true));

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            "property:IsTestProject=true",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_BlazorWebAssemblySdkIsHighConfidenceWebSubtype()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            BlazorWebAssemblySdk,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(ProjectClassificationSubtypes.BlazorWebAssembly, classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.BlazorWebAssemblySdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_BlazorWebAssemblyPackageHintDoesNotInferSubtype()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false)
        {
            PackageReferences = ["Microsoft.AspNetCore.Components.WebAssembly"]
        });

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_BlazorWebAssemblySdkWithWorkerSdkReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            [
                DeterministicProjectClassifier.BlazorWebAssemblySdk,
                DeterministicProjectClassifier.WorkerSdk
            ],
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Contains(
            $"sdk:{DeterministicProjectClassifier.BlazorWebAssemblySdk}",
            classification.Signals);
        Assert.Contains(
            $"sdk:{DeterministicProjectClassifier.WorkerSdk}",
            classification.Signals);
        Assert.Contains("conflict:specialized-sdk", classification.Signals);
    }

    [Fact]
    public void Classify_BlazorWebAssemblySdkWithWorkerOptInReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            BlazorWebAssemblySdk,
            "Exe",
            false)
        {
            UsingMicrosoftNETSdkWorker = true
        });

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Contains(
            $"sdk:{DeterministicProjectClassifier.BlazorWebAssemblySdk}",
            classification.Signals);
        Assert.Contains(
            "property:UsingMicrosoftNETSdkWorker=true",
            classification.Signals);
        Assert.Contains("conflict:web-worker", classification.Signals);
    }

    [Fact]
    public void Classify_WorkerSdkIsRecognizedBeforeOutputType()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WorkerSdk,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WorkerSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_WorkerOptInPropertyIsHighConfidenceWorkerSignal()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Library",
            false)
        {
            UsingMicrosoftNETSdkWorker = true
        });

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            "property:UsingMicrosoftNETSdkWorker=true",
            Assert.Single(classification.Signals));
    }

    [Theory]
    [InlineData(DeterministicProjectClassifier.MicrosoftExtensionsHostingSystemdPackage)]
    [InlineData(DeterministicProjectClassifier.MicrosoftExtensionsHostingWindowsServicesPackage)]
    public void Classify_ExecutableWithServiceLifetimePackageIsMediumConfidenceWorker(
        string packageReference)
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Exe",
            false)
        {
            PackageReferences = [packageReference]
        });

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal($"package:{packageReference}", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_GenericHostingPackageAloneRemainsConsole()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Exe",
            false)
        {
            PackageReferences = ["Microsoft.Extensions.Hosting"]
        });

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal("property:OutputType=Exe", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_WebSdkOverridesServiceLifetimePackage()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false)
        {
            PackageReferences =
            [
                DeterministicProjectClassifier.MicrosoftExtensionsHostingWindowsServicesPackage
            ]
        });

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_ExecutableWithoutSpecializedSdkIsConsole()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal("property:OutputType=Exe", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_LibraryOutputTypeIsLibrary()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Library",
            false));

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:OutputType=Library", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_ConflictingSpecializedSdksReturnUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebAndWorkerSdks,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Contains("conflict:specialized-sdk", classification.Signals);
        Assert.Contains($"sdk:{DeterministicProjectClassifier.WebSdk}", classification.Signals);
        Assert.Contains($"sdk:{DeterministicProjectClassifier.WorkerSdk}", classification.Signals);
    }

    [Fact]
    public void Classify_WebSdkWithWorkerOptInPropertyReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false)
        {
            UsingMicrosoftNETSdkWorker = true
        });

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Contains($"sdk:{DeterministicProjectClassifier.WebSdk}", classification.Signals);
        Assert.Contains("property:UsingMicrosoftNETSdkWorker=true", classification.Signals);
        Assert.Contains("conflict:web-worker", classification.Signals);
    }

    [Fact]
    public void Classify_ServiceLifetimePackageWithoutExecutableOutputRemainsConservative()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Library",
            false)
        {
            PackageReferences =
            [
                DeterministicProjectClassifier.MicrosoftExtensionsHostingSystemdPackage
            ]
        });

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:OutputType=Library", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_UnsupportedExecutableShapeReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "WinExe",
            false));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Equal("property:OutputType=WinExe", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_MissingEvidenceReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            null,
            null));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Empty(classification.Signals);
    }

    [Fact]
    public void Classify_IsDeterministicAcrossSdkOrderCasingAndDuplicates()
    {
        var first = _classifier.Classify(new ProjectClassificationFacts(
            new[]
            {
                "microsoft.net.sdk.web",
                "Microsoft.NET.Sdk.Web"
            },
            " exe ",
            false));
        var second = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false));

        Assert.Equal(second, first);
    }

    [Fact]
    public void Classify_OtherKindsDoNotPopulateConcreteSubtypes()
    {
        var classifications = new[]
        {
            _classifier.Classify(new ProjectClassificationFacts(WebSdk, "Exe", false)),
            _classifier.Classify(new ProjectClassificationFacts(WorkerSdk, "Exe", false)),
            _classifier.Classify(new ProjectClassificationFacts(Array.Empty<string>(), "Exe", false)),
            _classifier.Classify(new ProjectClassificationFacts(Array.Empty<string>(), "Library", false)),
            _classifier.Classify(new ProjectClassificationFacts(Array.Empty<string>(), null, null))
        };

        Assert.All(classifications, classification => Assert.Null(classification.Subtype));
    }
}
