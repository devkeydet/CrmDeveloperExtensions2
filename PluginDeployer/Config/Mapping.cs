﻿using CrmDeveloperExtensions2.Core;
using CrmDeveloperExtensions2.Core.Models;
using EnvDTE;
using System.Collections.Generic;
using System.Linq;

namespace PluginDeployer.Config
{
    public static class Mapping
    {
        public static PluginDeployConfig GetSpklPluginConfig(Project project, string profile)
        {
            string projectPath = CrmDeveloperExtensions2.Core.Vs.ProjectWorker.GetProjectPath(project);
            SpklConfig spklConfig = CrmDeveloperExtensions2.Core.Config.Mapping.GetSpklConfigFile(projectPath, project);

            List<PluginDeployConfig> spklPluginDeployConfigs = spklConfig.plugins;
            if (spklPluginDeployConfigs == null)
                return null;

            return profile.StartsWith(ExtensionConstants.NoProfilesText)
                ? spklPluginDeployConfigs[0]
                : spklPluginDeployConfigs.FirstOrDefault(p => p.profile == profile);
        }
    }
}