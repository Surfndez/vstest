﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CommandLine.Processors
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    using Microsoft.VisualStudio.TestPlatform.CommandLine.Processors.Utilities;
    using Microsoft.VisualStudio.TestPlatform.Common;
    using Microsoft.VisualStudio.TestPlatform.Common.Interfaces;
    using Microsoft.VisualStudio.TestPlatform.Common.Utilities;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Utilities;
    using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions;
    using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
    using Microsoft.VisualStudio.TestPlatform.Utilities;

    using CommandLineResources = Microsoft.VisualStudio.TestPlatform.CommandLine.Resources.Resources;

    internal class EnableBlameArgumentProcessor : IArgumentProcessor
    {
        /// <summary>
        /// The name of the command line argument that the ListTestsArgumentExecutor handles.
        /// </summary>
        public const string CommandName = "/Blame";

        private Lazy<IArgumentProcessorCapabilities> metadata;

        private Lazy<IArgumentExecutor> executor;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnableBlameArgumentProcessor"/> class.
        /// </summary>
        public EnableBlameArgumentProcessor() 
        {
        }

        public Lazy<IArgumentProcessorCapabilities> Metadata
        {
            get
            {
                if (this.metadata == null)
                {
                    this.metadata = new Lazy<IArgumentProcessorCapabilities>(() => new EnableBlameArgumentProcessorCapabilities());
                }

                return this.metadata;
            }
        }

        /// <summary>
        /// Gets or sets the executor.
        /// </summary>
        public Lazy<IArgumentExecutor> Executor
        {
            get
            {
                if (this.executor == null)
                {
                    this.executor = new Lazy<IArgumentExecutor>(() => new EnableBlameArgumentExecutor(RunSettingsManager.Instance, new PlatformEnvironment()));
                }

                return this.executor;
            }
            set
            {
                this.executor = value;
            }
        }
    }

    /// <summary>
    /// The argument capabilities.
    /// </summary>
    internal class EnableBlameArgumentProcessorCapabilities : BaseArgumentProcessorCapabilities
    {
        public override string CommandName => EnableBlameArgumentProcessor.CommandName;

        public override bool AllowMultiple => false;

        public override bool IsAction => false;

        public override ArgumentProcessorPriority Priority => ArgumentProcessorPriority.Logging;

        public override string HelpContentResourceName => CommandLineResources.EnableBlameUsage;

        public override HelpContentPriority HelpPriority => HelpContentPriority.EnableDiagArgumentProcessorHelpPriority;
    }

    /// <summary>
    /// The argument executor.
    /// </summary>
    internal class EnableBlameArgumentExecutor : IArgumentExecutor
    {
        /// <summary>
        /// Blame logger and data collector friendly name
        /// </summary>
        private static string BlameFriendlyName = "blame";

        /// <summary>
        /// Run settings manager
        /// </summary>
        private IRunSettingsProvider runSettingsManager;

        /// <summary>
        /// Platform environment
        /// </summary>
        private IEnvironment environment;

        #region Constructor

        internal EnableBlameArgumentExecutor(IRunSettingsProvider runSettingsManager, IEnvironment environment)
        {
            this.runSettingsManager = runSettingsManager;
            this.environment = environment;
            this.Output = ConsoleOutput.Instance;
        }

        #endregion

        #region Properties

        internal IOutput Output { get; set; }

        #endregion

        #region IArgumentExecutor

        /// <summary>
        /// Initializes with the argument that was provided with the command.
        /// </summary>
        /// <param name="argument">Argument that was provided with the command.</param>
        public void Initialize(string argument)
        {
            bool isDumpEnabled = false;

            var parseSucceeded = LoggerUtilities.TryParseLoggerArgument(argument, out string loggerIdentifier, out Dictionary<string, string> parameters);
            if (parseSucceeded && loggerIdentifier.Equals(Constants.BlameCollectDumpKey, StringComparison.OrdinalIgnoreCase))
            {
                if (this.environment.OperatingSystem == PlatformOperatingSystem.Windows &&
                    this.environment.Architecture != PlatformArchitecture.ARM64 &&
                    this.environment.Architecture != PlatformArchitecture.ARM)
                {
                    isDumpEnabled = true;
                }
                else
                {
                    Output.Warning(false, CommandLineResources.BlameCollectDumpNotSupportedForPlatform);
                }
            }

            // Add Blame Logger
            EnableLoggerArgumentExecutor.AddLoggerToRunSettings(BlameFriendlyName, this.runSettingsManager);

            // Add Blame Data Collector
            CollectArgumentExecutor.AddDataCollectorToRunSettings(BlameFriendlyName, this.runSettingsManager);

            // Get results directory from RunSettingsManager
            var runSettings = this.runSettingsManager.ActiveRunSettings;
            string resultsDirectory = null;
            if (runSettings != null)
            {
                try
                {
                    RunConfiguration runConfiguration = XmlRunSettingsUtilities.GetRunConfigurationNode(runSettings.SettingsXml);
                    resultsDirectory = RunSettingsUtilities.GetTestResultsDirectory(runConfiguration);
                }
                catch (SettingsException se)
                {
                    if (EqtTrace.IsErrorEnabled)
                    {
                        EqtTrace.Error("EnableBlameArgumentProcessor: Unable to get the test results directory: Error {0}", se);
                    }
                }
            }

            // Add configuration element
            var settings = runSettings?.SettingsXml;
            if (settings == null)
            {
                runSettingsManager.AddDefaultRunSettings();
                settings = runSettings?.SettingsXml;
            }

            var dataCollectionRunSettings = XmlRunSettingsUtilities.GetDataCollectionRunSettings(settings);
            if (dataCollectionRunSettings == null)
            {
                dataCollectionRunSettings = new DataCollectionRunSettings();
            }

            var XmlDocument = new XmlDocument();
            var outernode = XmlDocument.CreateElement("Configuration");
            var node = XmlDocument.CreateElement("ResultsDirectory");
            outernode.AppendChild(node);
            node.InnerText = resultsDirectory;

            if (isDumpEnabled)
            {
                var dumpNode = XmlDocument.CreateElement(Constants.BlameCollectDumpKey);
                if (parameters != null && parameters.Count > 0)
                {
                    foreach (KeyValuePair<string, string> entry in parameters)
                    {
                        var attribute = XmlDocument.CreateAttribute(entry.Key);
                        attribute.Value = entry.Value;
                        dumpNode.Attributes.Append(attribute);
                    }
                }
                outernode.AppendChild(dumpNode);
            }

            foreach (var item in dataCollectionRunSettings.DataCollectorSettingsList)
            {
                if( item.FriendlyName.Equals(BlameFriendlyName))
                {
                    item.Configuration = outernode;
                }
            }

            runSettingsManager.UpdateRunSettingsNodeInnerXml(Constants.DataCollectionRunSettingsName, dataCollectionRunSettings.ToXml().InnerXml);
        }

        /// <summary>
        /// Executes the argument processor.
        /// </summary>
        /// <returns>The <see cref="ArgumentProcessorResult"/>.</returns>
        public ArgumentProcessorResult Execute()
        {
            // Nothing to do since we updated the logger and data collector list in initialize
            return ArgumentProcessorResult.Success;
        }

        #endregion
    }
}
