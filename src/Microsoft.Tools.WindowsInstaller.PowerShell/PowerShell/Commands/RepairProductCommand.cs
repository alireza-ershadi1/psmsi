﻿// Copyright (C) Microsoft Corporation. All rights reserved.
//
// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
// KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.

using Microsoft.Deployment.WindowsInstaller;
using Microsoft.Tools.WindowsInstaller.Properties;
using System.Management.Automation;

namespace Microsoft.Tools.WindowsInstaller.PowerShell.Commands
{
    /// <summary>
    /// The Repair-MSIProduct cmdlet.
    /// </summary>
    [Cmdlet(VerbsDiagnostic.Repair, "MSIProduct", DefaultParameterSetName = ParameterSet.Path)]
    [OutputType(typeof(ProductInstallation))]
    public sealed class RepairProductCommand : InstallProductCommandBase<RepairProductActionData>
    {
        private ReinstallModesConverter converter;

        /// <summary>
        /// Creates a new instance of the <see cref="RepairProductCommand"/> with the default <see cref="ReinstallMode"/>.
        /// </summary>
        public RepairProductCommand()
        {
            this.ReinstallMode = RepairProductActionData.Default;
            this.converter = new ReinstallModesConverter();
        }

        /// <summary>
        /// Gets or sets the <see cref="ReinstallModes"/> to use for repairing the product.
        /// </summary>
        /// <value>The default value is <see cref="RepairProductActionData.Default"/>.</value>
        [Parameter(ValueFromPipelineByPropertyName = true)]
        [ReinstallMode]
        public ReinstallModes ReinstallMode { get; set; }

        /// <summary>
        /// Gets or sets whether installed product information should be returned.
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru { get; set; }

        /// <summary>
        /// Gets a generic description of the activity performed by this cmdlet.
        /// </summary>
        protected override string Activity
        {
            get { return Resources.Action_Repair; }
        }

        /// <summary>
        /// Repairs a product given the provided <paramref name="data"/>.
        /// </summary>
        /// <param name="data">An <see cref="RepairProductActionData"/> with information about the package to install.</param>
        protected override void ExecuteAction(RepairProductActionData data)
        {
            string mode = this.converter.ConvertToString(data.ReinstallMode);
            data.CommandLine += " REINSTALL=ALL REINSTALLMODE=" + mode;

            if (!string.IsNullOrEmpty(data.Path))
            {
                Installer.InstallProduct(data.Path, data.CommandLine);
            }
            else if (!string.IsNullOrEmpty(data.ProductCode))
            {
                Installer.ConfigureProduct(data.ProductCode, INSTALLLEVEL_DEFAULT, InstallState.Default, data.CommandLine);
            }

            if (this.PassThru)
            {
                var product = ProductInstallation.GetProducts(data.ProductCode, null, UserContexts.All).FirstOrDefault();
                if (null != product && product.IsInstalled)
                {
                    this.WriteObject(product.ToPSObject(this.SessionState.Path));
                }
            }
        }

        /// <summary>
        /// Updates the <see cref="RepairProductActionData"/> to include the current <see cref="ReinstallMode"/> flags.
        /// </summary>
        /// <param name="data">The <see cref="RepairProductActionData"/> to update.</param>
        protected override void UpdateAction(RepairProductActionData data)
        {
            base.UpdateAction(data);

            data.ReinstallMode = this.ReinstallMode;
        }
    }
}