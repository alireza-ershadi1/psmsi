﻿// The MIT License (MIT)
//
// Copyright (c) Microsoft Corporation
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.Deployment.WindowsInstaller;

namespace Microsoft.Tools.WindowsInstaller.PowerShell.Commands
{
    /// <summary>
    /// Base class for source list cmdlets.
    /// </summary>
    public abstract class SourceCommandBase : PSCmdlet
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SourceCommandBase"/> class.
        /// </summary>
        protected SourceCommandBase()
        {
            this.PreviousParameters = new ParametersCollection();
            this.UserContext = UserContexts.All;
        }

        /// <summary>
        /// Gets or sets the ProductCode for a product or patch to which the source is registered.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
        public string ProductCode { get; set; }

        /// <summary>
        /// Gets or sets the optional patch code for a patch to which the source is registered.
        /// </summary>
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public string PatchCode { get; set; }

        /// <summary>
        /// Gets or sets the user SID of the product or patch.
        /// </summary>
        [Alias("User")]
        [Sid]
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public string UserSid { get; set; }

        /// <summary>
        /// Gets or sets the user context of the product or patch.
        /// </summary>
        [Alias("Context", "InstallContext")]
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public UserContexts UserContext { get; set; }

        /// <summary>
        /// Gets the list of previous parameters to work around a reentrency problem with Windows Installer.
        /// </summary>
        protected ParametersCollection PreviousParameters { get; private set; }

        /// <summary>
        /// Adds current parameters to <see cref="PreviousParameters"/> towork around a reentrency problem with Windows Installer.
        /// </summary>
        protected sealed override void ProcessRecord()
        {
            var param = new Parameters
            {
                ProductCode = this.ProductCode,
                PatchCode = this.PatchCode,
                UserSid = this.UserSid,
                UserContext = this.UserContext,
            };

            this.UpdateParameters(param);
            this.PreviousParameters.Add(param);
        }

        /// <summary>
        /// Allows child classes to update the <see cref="Parameters"/>.
        /// </summary>
        /// <param name="param">The <see cref="Parameters"/> to update.</param>
        protected virtual void UpdateParameters(Parameters param)
        {
        }

        /// <summary>
        /// Child classes must override and should use the <see cref="PreviousParameters"/> for processing records.
        /// </summary>
        protected abstract override void EndProcessing();

        /// <summary>
        /// Gets the <see cref="Installation"/> class given the <see cref="Parameters"/>.
        /// </summary>
        /// <param name="param">The <see cref="Parameters"/> of the <see cref="Installation"/> to get.</param>
        /// <returns>An <see cref="Installation"/> given the <see cref="Parameters"/>.</returns>
        protected Installation GetInstallation(Parameters param)
        {
            Installation installation = null;

            try
            {
                if (string.IsNullOrEmpty(param.PatchCode))
                {
                    installation = ProductInstallation.GetProducts(param.ProductCode, param.UserSid, param.UserContext).FirstOrDefault();
                }
                else
                {
                    installation = PatchInstallation.GetPatches(param.PatchCode, param.ProductCode, param.UserSid, param.UserContext, PatchStates.All).FirstOrDefault();
                }
            }
            catch (InstallerException ex)
            {
                var pse = new PSInstallerException(ex);
                if (null != pse.ErrorRecord)
                {
                    base.WriteError(pse.ErrorRecord);
                }
            }

            return installation;
        }

        /// <summary>
        /// Writes a source information to the pipeline.
        /// </summary>
        /// <param name="installation">The <see cref="Installation"/> to which the source is registered.</param>
        protected void WriteSourceList(Installation installation)
        {
            ProductInstallation product = null;
            PatchInstallation patch = null;
            var order = 0;

            if (installation is ProductInstallation)
            {
                product = (ProductInstallation)installation;
            }
            else if (installation is PatchInstallation)
            {
                patch = (PatchInstallation)installation;
            }

            try
            {
                foreach (var source in installation.SourceList)
                {
                    SourceInfo info = null;
                    if (null != product)
                    {
                        info = new SourceInfo(product.ProductCode, product.UserSid, product.Context, source, order++);
                    }
                    else if (null != patch)
                    {
                        info = new PatchSourceInfo(patch.ProductCode, patch.PatchCode, patch.UserSid, patch.Context, source, order++);
                    }

                    if (null != info)
                    {
                        this.WriteObject(info);
                    }
                }
            }
            catch (InstallerException ex)
            {
                if (NativeMethods.ERROR_BAD_CONFIGURATION == ex.ErrorCode)
                {
                    var code = null != product ? product.ProductCode : null != patch ? patch.PatchCode : string.Empty;
                    var message = string.Format(Properties.Resources.Error_Corrupt, code);
                    var exception = new Exception(message, ex);

                    var error = new ErrorRecord(exception, "Error_Corrupt", ErrorCategory.NotInstalled, installation);
                    base.WriteError(error);
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Parameters persisted to work around a Windows Installer reentrency issue.
        /// </summary>
        protected class Parameters
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Parameters"/> class.
            /// </summary>
            internal Parameters()
            {
                this.Paths = new List<string>();
            }

            /// <summary>
            /// Gets the ProductCode parameter.
            /// </summary>
            public string ProductCode { get; internal set; }

            /// <summary>
            /// Gets the PatchCode parameter.
            /// </summary>
            public string PatchCode { get; internal set; }

            /// <summary>
            /// Gets the UserSid parameter.
            /// </summary>
            public string UserSid { get; internal set; }

            /// <summary>
            /// Gets the UserContext parameter.
            /// </summary>
            public UserContexts UserContext { get; internal set; }

            /// <summary>
            /// Gets the Path or LiteralPath parameters.
            /// </summary>
            public IList<string> Paths { get; private set; }
        }

        /// <summary>
        /// A collection of <see cref="Parameters"/> indexed by their <see cref="Parameters.PatchCode"/> or <see cref="Parameters.ProductCode"/>.
        /// </summary>
        protected class ParametersCollection : KeyedCollection<string, Parameters>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ParametersCollection"/> class.
            /// </summary>
            public ParametersCollection()
                : base(StringComparer.OrdinalIgnoreCase)
            {
            }

            /// <summary>
            /// Adds a new <see cref="Parameters"/> instance or updates the <see cref="Parameters.Paths"/> of an existing one.
            /// </summary>
            /// <param name="param">The <see cref="Parameters"/> instance to add or merge into an existing one.</param>
            public new void Add(Parameters param)
            {
                var key = this.GetKeyForItem(param);
                if (this.Contains(key))
                {
                    var existing = this[key];
                    foreach (var path in param.Paths)
                    {
                        existing.Paths.Add(path);
                    }
                }
                else
                {
                    base.Add(param);
                }
            }

            /// <summary>
            /// Gets the <see cref="Parameters.PatchCode"/> or <see cref="Parameters.ProductCode"/>.
            /// </summary>
            /// <param name="item">The <see cref="Parameters"/> from which the key is derived.</param>
            /// <returns>The key for the <see cref="Parameters"/> instance.</returns>
            protected override string GetKeyForItem(Parameters item)
            {
                return item.PatchCode ?? item.ProductCode;
            }
        }
    }
}
