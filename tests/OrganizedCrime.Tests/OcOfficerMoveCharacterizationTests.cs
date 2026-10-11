using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>OC-155: baseline source hashes guard engine-bound moves, including read,
/// side-effect, catch/finally, native-call, float, and log order. Only named receiver
/// prefixes and visibility/static scaffolding are normalized.</summary>
public sealed class OcOfficerMoveCharacterizationTests
{
    [Theory]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryEnsureTemplate", "f9a4055bca5a7902ea39e490ab7af61e81f446e5cc31f69b792e8b5e9a36a01b")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryDestroyTemplate", "edc9ab3a70ac927210be0f2996f960cee85a7059211166218b735c7a2329d58e")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryReadTemplateStatus", "a3ff6be19a7f10c8e0ade23424af6a169a53986ff699307301dfb4ef1de09868")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TemplateValid", "98d95aa207c4fcf8107ed1ef7ade12c711d51346c3c4a8338a314844553f9928")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "DestroyTemplateObject", "d470674e9531eb06fe84263a026772ad6991ba9f6c066a34f31e8671d25be582")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryConfirmStableSource", "ab7d8987c81203b7ff4d2a4705495ffe0f26929acab64d65ef222a8130c99526")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "FaultTemplate", "35b60e9f97919fa4c725ec6bd8d61481d370b7ec56b32583f4179dc1fea4649b")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryFindSource", "3f60aa5b00182f899acd67e2c37379c588eb33e290bea5cededff9c39dadb639")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TemplateOfficerPointer", "fbfbe1d80992619ae79ded6a3f64aae8118e85cd40cc85ff3cec82f53a23657b")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "IsPooled", "ad1aed545c9603692127aa7c9fe01fc24048234b576830cf2058d491f76655f0")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "IsAvatarActive", "abc41c8c389ef9d128e9a8e68f95480873e32b69d93fdca29bc153c6b9d7fbb1")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "HasAppearanceData", "579af37de8faff0ba055a31cb7b51aa5afe342401b6df8ee216755375c8ec711")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryChooseCloneRoot", "631eb368631e482a50ab18bdfde120470a9d28b1eaea941f2688d6e0828ac242")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "OfficerReferences", "809a4cf405bcfd8622a9991c74115319f119cb4934ca79a8337873e85e0ff55c")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryBuildTemplate", "ffd8e47657de2640e32ab901b860251fb0c687b24043072a025ed51c4df83b78")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "PrefabResolves", "385d5786018d940e8f1999853219b9cf938575697dfbe8ab07a9fce47fdfbdd1")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "TryWriteTemplateId", "32c9e0d70c9d2d3a9b6c848f70ff2e771560584e2692d44bb95ab9c7ce729c3b")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerTemplateBuilder.cs", "DetailSuffix", "589130c549773f05a0d1e47611e53915ad66068a87110a916c5dc77e1146e01a")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "TrySpawnClone", "86f90969db7f31664b30d61def84e9d55e8e02a54e95eb0506a3485b50b49de3")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "TryRemoveClone", "c2d3d81e95649b3ba5857c10d53b6d06c2ddacd7b0bd2feed6f03d5c6650251c")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "BindMovement", "4f18b219344b40254ea1de47f33c295b4b46d50a8e1d06cbebf8daed8e583c24")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "BindSpeedController", "2de43ab5da9df9b514c48a35a2a3d2fb327ce6a2ad1160e18cb6d8f8eb48f905")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "WakeTargets", "d029b96b55e500ee4b6d8b7b7c059e79ea550f879372e8368e11605bd2eb942d")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "WakeTarget", "84a9100ce32cd1aa752cc29a69932229f2063bee3f12a745bc643e5f4bad5dbf")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "ValidateOwned", "8157865782b5a61ad659bf37432d266ea3f0fcd3998a7060ec4fbf0b0ae5d47d")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "IsInside", "fc56d7ab4f21eb7af4fc64c02b6610df980b11df154b633df9dcd6965963fe1c")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "TrySelectTopNetwork", "e853c370171cf538ef809445204dfdd33c3fc7957451ddf60802efc917466c85")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "NearestNetworkAncestor", "a7f7cfde249edea2951252c310acc9cde2793207fce61a41275dbab930c85612")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "ValidateAwake", "680c3c4f0668d86c964706782274d44532b746c37f45e7201b68707b20f908e3")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "TryAssignCloneId", "4b29614a56867e09f7e6fd068db0174b6ead9dbb1ec489a8cdd103a439844bdf")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "RemoveSpawnedClone", "d06e55d40df2d931676b485b5413ba4ba28ab452d2648de608f96bdfff6971a2")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "RemoveFromStationPools", "b369c3531207dd0776e5ef0c4c71163e7d69c2ace28750c399e478ebde9efe4e")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "RemoveFromOwnCheckpoint", "5151ffb7ebcea8d2482c08a13a58cdfbe0b70e270ea0d2391150f4f7bfafef68")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "RemoveFromOfficers", "7d330c5f994918d001a9f7bd0ee1056592e19e2aeadf87e3a343515162481138")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "RemoveFromRegistry", "5eb029fda9c1e88a06136cbe9beb501ecf469d8579aadd6d64ed317509cd416c")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "RemovePointer", "1fd69b413df729002374e4341b75cd964f7066784de26b72bc3a65e903fb504e")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs", "TryGetOfficer", "98b13e881123e616108cf2f4b960751365cf1e14551ba8651d2bcaf403c52118")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "TryBindToCheckpoint", "71b291865b93a1f30f4d5374529770d58839130db28a46bbe13aaead683f7992")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "EnsureCheckpointReady", "141cd6164abd3a2e98797c07dba442ad4f35627290a3d56a5603ce6e849dac54")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "DisableCopiedBehaviours", "7b8db8449561cba1b53bc657feb434b000e330f1df0fe00b37b8b5433004e332")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "FindScheduleBehaviour", "af500a0e9dcc7ae13d9b19f5aaed0d9645fae7aeccc0451e715c2e68e3382f66")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "DisableIfEnabled", "abe05e8e0377d3369249a4863c1659277c47861fd9a1955fd6f93ff87afe997e")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "IsActive", "6f433b95eabac470408ad9c86f3ed18925720a88869f022e35b042e366c0b584")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "PointsAt", "893975bb6b8f545f61c12aed5193bbadb77d3b802c5856a149193ea9e060466e")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "UnbindCloneForRemove", "7bb104e9471db48285a854e8609637fd355e9a460a3aea8ed56bf138754d5737")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "SetCheckpointDialogue", "10fda148e9a16afa35aaf685363d641f0ba3176ea7e03755549b08b8e529b9da")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "TryClearSwapMap", "4b762e275dcdd43f24947fea83e10707478c2932079b90dbde9a67732b863751")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDutyBinder.cs", "TryUnmapClone", "ab40822de9f152c67161aba7231c1c0aa6661153715ea4d19d3d03110a1a9a06")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadGameMinutes", "d5c7c2e27fc7514bac70a641586df1fe5d514602450515a7be529524167592e1")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadKnockedOut", "63e82467e5c39f3610b89d7c3f7baf73cea9fac9d276778e677c24251c668b80")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadClonePosition", "774737998e77f748cfdaf23d7503a46bf4b9dc3d98f3b0e6e14b683bd1621061")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadBehaviourState", "ed0c865a16e32ca3514e610e802e79a216500a0507d44fc7d40dbf64f4b7ff87")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadBoundState", "2b6ab4ff7f8c76a778c24aeb2e4e740ac1cd851fc2f04b0a6ba6b70a6dd000a0")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadBehaviourDiagnostics", "177f40e4ada54ffc58153b12eab09a4b0c8ca6c10dd9435a22498c1d5cace8f3")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "ReadBehaviourInfo", "b5ff7e35be3c99a6e5f26b9142c6bf53faddd00a2a1e6a66a17ca53181524fd9")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "ClassifyCheckpoint", "518ff29a4bf031bc8db1e282b4b0e1402d375eef957ce24bc6c0bea196c338d1")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadNativeLeak", "0512fe676645cd001253244dc59e1490a673756374990b19bff4e3cc522d18f5")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryParseCheckpointLocation", "ee5d1841935affef661ebbcb8d8f7c561d82f2700aff7b465849ad566a45eaea")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadNativeCounts", "08e7643d4b9df0ef93f469155faa17a6b6cc32a1bb46ffe5bf38c510e9609432")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "TryReadPlayerPose", "1329f5cb110ca7f57534d55ceef616d344c57a1c890856b00197912116b2f7c6")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "SnapshotApplyInputs", "9f55335d2029bc57fae4902d89aaa561769add6a2d43680b8cecf63ea0a7e854")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "Presence", "9ab428f33983ed423ec79c031109d0571413ba2dd98795c146ca88234d4972d4")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "CountPointer", "a74ea2a3b4d597f6dd3aec36e1b5d6f04f750b82971a83cc0e5379cff25f4d93")]
    [InlineData("tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs", "tools/OrganizedCrime/Runtime/Police/OcOfficerDiagnostics.cs", "ContainsPointer", "9b04623acebf34ea5ccf69c2f2b20d1858a3e2979e4530594cf0f9ce85b7d8e2")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "TryAddLampAbovePlayer", "a556705104483f325d2a37c172c0adcf8f71de8ac2bd463adf93570b6661e8cb")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "TryRemoveMostRecentLamp", "e369353a66074781f73fc2c51e7889fc8d55db2011d99ce288ffca2baf65376c")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "TryAdjustMostRecentLampHeight", "362827355f1fe637bb93346879c98332e13a9c112e982f9e4deb1e39278a18f1")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "TryAdjustAllLampIntensity", "7f1d89b2f8bff7682a47a0c2ec0ef7b1c2bba9d7cc0c4ee2180a1465d841340f")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "TryToggleMostRecentLampYaw", "1d78566071f0f1a793988f16b8445bdd249dfebdc30a815e050bc2f1071bbf37")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "TryLogLampLayout", "230417f78507a886b643ce646b38cdf7a91ad138fca9ec39368db52786848abb")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "SnapLighting", "82ef899062a7e0e650d86c400561ad0bca19cdb5fb6c1c6b244e528568b2192c")]
    [InlineData("tools/OrganizedCrime/Runtime/RuntimePropertyHost.cs", "tools/OrganizedCrime/Runtime/Qa/FishWarehouseLampTuningQa.cs", "ClampLighting", "b63333e313056ab86213400bc214d6d88145143477d96595156764d0a3074131")]
    public void Moved_member_matches_the_pre_refactor_source(string oldPath, string newPath, string member, string expected)
    {
        var root = FindRepositoryRoot();
        var moved = Path.Combine(root, newPath);
        var source = File.ReadAllText(File.Exists(moved) ? moved : Path.Combine(root, oldPath)).Replace("\r", "");
        var signature = Regex.Match(source, @"^    (?:public|private|internal) (?:static )?[^\n=]+?\b" + member + @"(?:<[^\n]+?>)?\([^\n]*", RegexOptions.Multiline);
        Assert.True(signature.Success, $"No member {member} in the mapped source.");
        var rest = source[signature.Index..];
        var firstBrace = rest.IndexOf("\n    {", StringComparison.Ordinal);
        var firstArrow = rest.IndexOf("=>", StringComparison.Ordinal);
        var end = firstArrow >= 0 && (firstBrace < 0 || firstArrow < firstBrace)
            ? rest.IndexOf(';') + 1
            : Regex.Match(rest, @"^    \};?$", RegexOptions.Multiline) is { Success: true } close
                ? close.Index + close.Length : -1;
        Assert.True(end > 0, $"No end for {member}.");
        var body = Regex.Replace(rest[..end], @"\b(?:_context\.|_templates\.|OcOfficerTemplateBuilder\.|OcOfficerSpawner\.|OcOfficerDutyBinder\.|OcOfficerDiagnostics\.|host\.|RuntimePropertyHost\.)", "");
        body = Regex.Replace(body, @"^\s*(?:public|private|internal) (?:static )?", "").Trim();
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tools", "OrganizedCrime", "Mod.cs")))
                return dir.FullName;
        throw new InvalidOperationException("Could not find repository root.");
    }
}