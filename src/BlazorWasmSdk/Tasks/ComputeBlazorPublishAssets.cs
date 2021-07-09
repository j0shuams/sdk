﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.NET.Sdk.BlazorWebAssembly
{
    public class ComputeBlazorPublishAssets : Task
    {
        [Required]
        public ITaskItem[] ResolvedFilesToPublish { get; set; }

        [Required]
        public ITaskItem[] WasmAotAssets { get; set; }

        [Required]
        public ITaskItem[] ExistingAssets { get; set; }

        [Required]
        public bool TimeZoneSupport { get; set; }

        [Required]
        public bool InvariantGlobalization { get; set; }

        [Required]
        public bool CopySymbols { get; set; }

        [Required]
        public string PublishPath { get; set; }

        [Required]
        public bool LinkerEnabled { get; set; }

        [Required]
        public bool AotEnabled { get; set; }

        [Output]
        public ITaskItem[] NewCandidates { get; set; }

        [Output]
        public ITaskItem[] UpdatedCandidates { get; set; }

        [Output]
        public ITaskItem[] FilesToRemove { get; set; }

        public override bool Execute()
        {
            var filesToRemove = new List<ITaskItem>();
            var newAssets = new List<ITaskItem>();
            var updatedAssets = new List<ITaskItem>();

            try
            {
                for (int i = 0; i < ResolvedFilesToPublish.Length; i++)
                {
                    var candidate = ResolvedFilesToPublish[i];
                    if (ComputeBlazorBuildAssets.ShouldFilterCandidate(candidate, TimeZoneSupport, InvariantGlobalization, CopySymbols, out var reason))
                    {
                        Log.LogMessage("Skipping asset '{0}' becasue '{1}'", candidate.ItemSpec, reason);
                        filesToRemove.Add(candidate);
                        continue;
                    }

                    if (LinkerEnabled && candidate.GetMetadata("Extension") == ".dll")
                    {
                        var existingCandidateAsset = FindCandidateAsset(candidate, "runtime");
                        UpdateAssetLists(filesToRemove, newAssets, updatedAssets, candidate, existingCandidateAsset);
                    }
                }

                if (AotEnabled)
                {
                    foreach (var aotAsset in WasmAotAssets)
                    {
                        var existingCandidateAsset = FindCandidateAsset(aotAsset, "native");
                        UpdateAssetLists(filesToRemove, newAssets, updatedAssets, aotAsset, existingCandidateAsset);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex.ToString());
                return false;
            }

            FilesToRemove = filesToRemove.ToArray();
            NewCandidates = newAssets.ToArray();
            UpdatedCandidates = updatedAssets.ToArray();

            return !Log.HasLoggedErrors;
        }

        private void UpdateAssetLists(
            List<ITaskItem> filesToRemove,
            List<ITaskItem> newAssets, 
            List<ITaskItem> updatedAssets,
            ITaskItem candidate,
            ITaskItem existingCandidateAsset)
        {
            if (existingCandidateAsset == null)
            {
                Log.LogError($"Unable to find existing static web asset for candidate '{candidate.ItemSpec}'.");
            }
            else
            {
                Log.LogMessage("Found existing static web asset '{0}' for candidate '{1}'.", existingCandidateAsset.ItemSpec, candidate.ItemSpec);

                // Since we found the original asset, we want to do three things:
                // * Remove the asset from the list of assets to publish
                // * Create a new asset that represents the linked dll.
                // * Update the existing static web asset to be Kind=Build so that it doesn't get included in the manifest

                filesToRemove.Add(candidate);

                var newAsset = new TaskItem(candidate.GetMetadata("FullPath"), existingCandidateAsset.CloneCustomMetadata());
                newAsset.SetMetadata("AssetKind", "Publish");
                newAsset.SetMetadata("ContentRoot", Path.Combine(PublishPath, "wwwroot"));
                newAsset.SetMetadata("CopyToOutputDirectory", "Never");
                newAsset.SetMetadata("CopyToPublishDirectory", "PreserveNewest");
                newAsset.SetMetadata("OriginalItemSpec", candidate.ItemSpec);
                newAssets.Add(newAsset);

                existingCandidateAsset.SetMetadata("AssetKind", "Build");
                existingCandidateAsset.SetMetadata("CopyToPublishDirectory", "Never");
                updatedAssets.Add(existingCandidateAsset);
            }
        }

        private ITaskItem FindCandidateAsset(ITaskItem candidate, string traitValue)
        {
            var fileName = Path.GetFileName(candidate.ItemSpec);
            Log.LogMessage("Looking for assets for candidate '{0}'", candidate.ItemSpec);
            ITaskItem specCandidate = null;
            var multipleSpecMatch = false;
            
            foreach (var asset in ExistingAssets)
            {
                if (!(string.Equals(asset.GetMetadata("AssetTraitName"), "BlazorWebAssemblyResource", StringComparison.Ordinal) &&
                    string.Equals(asset.GetMetadata("AssetTraitValue"), traitValue, StringComparison.Ordinal)))
                {
                    Log.LogMessage("Skipping asset '{0}' becasue AssetTraitName '{1}' or AssetTraitValue '{2}' do not match.", asset.ItemSpec, asset.GetMetadata("AssetTraitName"), asset.GetMetadata("AssetTraitValue"));
                    continue;
                }

                var relativePath = asset.GetMetadata("RelativePath");
                var assetFileName = Path.GetFileName(asset.GetMetadata("RelativePath"));
                if (string.Equals(fileName, assetFileName, StringComparison.Ordinal))
                {
                    Log.LogMessage("Found asset '{0}' with relative path file name match '{1}' for file '{2}'.", asset.ItemSpec, relativePath, fileName);
                    return asset;
                }
                else
                {
                    Log.LogMessage("Skipping asset '{0}' becasue file name '{1}' does not match '{2}'.", asset.ItemSpec, assetFileName, fileName);
                }

                // We fallback to matching on the spec filename only if we find an unique candidate.
                var assetSpecFileName = Path.GetFileName(asset.ItemSpec);
                if (!multipleSpecMatch && string.Equals(fileName, assetSpecFileName, StringComparison.Ordinal))
                {
                    if (specCandidate != null)
                    {
                        Log.LogMessage("Found multiple spec matches for '{0}' so results will be ignored.", fileName);
                        multipleSpecMatch = true;
                        specCandidate = null;
                    }
                    else
                    {
                        Log.LogMessage("Found asset '{0}' match '{1}' based on ItemSpec'.", asset.ItemSpec, assetSpecFileName);
                        specCandidate = asset;
                    }
                }
            }

            return specCandidate;
        }
    }
}
