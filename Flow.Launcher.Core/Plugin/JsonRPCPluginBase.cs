﻿using Flow.Launcher.Core.Resource;
using Flow.Launcher.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Infrastructure.Logger;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.Plugin;
using Microsoft.IO;
using System.Windows;
using System.Windows.Controls;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using CheckBox = System.Windows.Controls.CheckBox;
using Control = System.Windows.Controls.Control;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Documents;
using static System.Windows.Forms.LinkLabel;
using Droplex;
using System.Windows.Forms;
using Microsoft.VisualStudio.Threading;

namespace Flow.Launcher.Core.Plugin
{
    /// <summary>
    /// Represent the plugin that using JsonPRC
    /// every JsonRPC plugin should has its own plugin instance
    /// </summary>
    internal abstract class JsonRPCPluginBase : IAsyncPlugin, IContextMenu, ISettingProvider, ISavable
    {
        protected PluginInitContext Context;
        public const string JsonRPC = "JsonRPC";

        private int RequestId { get; set; }

        private string SettingConfigurationPath => Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "SettingsTemplate.yaml");

        private string SettingPath => Path.Combine(DataLocation.PluginSettingsDirectory, Context.CurrentPluginMetadata.Name, "Settings.json");

        public abstract List<Result> LoadContextMenus(Result selectedResult);

        private static readonly JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
#pragma warning disable SYSLIB0020
            // IgnoreNullValues is obsolete, but the replacement JsonIgnoreCondition.WhenWritingNull still 
            // deserializes null, instead of ignoring it and leaving the default (empty list). We can change the behaviour
            // to accept null and fallback to a default etc, or just keep IgnoreNullValues for now
            // see: https://github.com/dotnet/runtime/issues/39152
            IgnoreNullValues = true,
#pragma warning restore SYSLIB0020 // Type or member is obsolete
            Converters =
            {
                new JsonObjectConverter()
            }
        };

        private static readonly JsonSerializerOptions settingSerializeOption = new()
        {
            WriteIndented = true
        };

        private readonly Dictionary<string, FrameworkElement> _settingControls = new();

        protected abstract Task<bool> ExecuteResultAsync(JsonRPCResult result);
        protected abstract Task<List<Result>> QueryRequestAsync(JsonRPCRequestModel request, CancellationToken token);

        protected PortableSettings Settings { get; set; }

        protected List<Result> ParseResults(JsonRPCQueryResponseModel queryResponseModel)
        {
            if (queryResponseModel.Result == null) return null;

            if (!string.IsNullOrEmpty(queryResponseModel.DebugMessage))
            {
                Context.API.ShowMsg(queryResponseModel.DebugMessage);
            }

            foreach (var result in queryResponseModel.Result)
            {
                result.AsyncAction = async c =>
                {
                    Settings.UpdateSettings(result.SettingsChange);

                    return await ExecuteResultAsync(result);
                };
            }

            var results = new List<Result>();

            results.AddRange(queryResponseModel.Result);

            Settings.UpdateSettings(queryResponseModel.SettingsChange);

            return results;
        }

        protected void ExecuteFlowLauncherAPI(string method, object[] parameters)
        {
            var parametersTypeArray = parameters.Select(param => param.GetType()).ToArray();
            var methodInfo = typeof(IPublicAPI).GetMethod(method, parametersTypeArray);

            if (methodInfo == null)
            {
                return;
            }

            try
            {
                methodInfo.Invoke(Context.API, parameters);
            }
            catch (Exception)
            {
#if (DEBUG)
                throw;
#endif
            }
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            var request = new JsonRPCRequestModel(RequestId++,
                "query",
                new object[]
                {
                    query.Search
                },
                Settings.Inner);

            return await QueryRequestAsync(request, token);

        }


        private async Task InitSettingAsync()
        {
            if (!File.Exists(SettingConfigurationPath))
                return;

            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
            var configuration = deserializer.Deserialize<JsonRpcConfigurationModel>(await File.ReadAllTextAsync(SettingConfigurationPath));

            Settings ??= new PortableSettings
            {
                Configuration = configuration,
                SettingPath = SettingPath,
                API = Context.API
            };

        }

        public virtual async Task InitAsync(PluginInitContext context)
        {
            this.Context = context;
            await InitSettingAsync();
        }

        public void Save()
        {
            Settings.Save();
        }
        public Control CreateSettingPanel()
        {
            return Settings.CreateSettingPanel();
        }
    }

}