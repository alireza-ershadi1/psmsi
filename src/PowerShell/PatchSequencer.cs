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
using System.Globalization;
using System.IO;
using System.Xml.XPath;
using Microsoft.Deployment.WindowsInstaller;
using Microsoft.Deployment.WindowsInstaller.Package;
using Microsoft.Tools.WindowsInstaller.PowerShell;

namespace Microsoft.Tools.WindowsInstaller
{
    /// <summary>
    /// Determines patch applicability for given products.
    /// </summary>
    internal sealed class PatchSequencer
    {
        private delegate IEnumerable<PatchSequence> GetApplicablePatchesAction(string product, string userSid, UserContexts context);
        private static readonly string Namespace = @"http://www.microsoft.com/msi/patch_applicability.xsd";

        private GetApplicablePatchesAction action;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchSequencer"/> class.
        /// </summary>
        internal PatchSequencer()
        {
            this.Patches = new Set<string>(StringComparer.OrdinalIgnoreCase);
            this.TargetProductCodes = new Set<string>(StringComparer.OrdinalIgnoreCase);

            this.action = new GetApplicablePatchesAction(this.GetApplicablePatches);
        }

        /// <summary>
        /// Raised when an inapplicable patch is processed.
        /// </summary>
        internal event EventHandler<InapplicablePatchEventArgs> InapplicablePatch;

        /// <summary>
        /// Gets the list of all unique patch or patch XML file paths added to the sequencer.
        /// </summary>
        internal Set<string> Patches { get; private set; }

        /// <summary>
        /// Gets the list of all unique target ProductCodes for all patches or a given set.
        /// </summary>
        internal Set<string> TargetProductCodes { get; private set; }

        /// <summary>
        /// Adds the path to a patch or patch XML.
        /// </summary>
        /// <param name="path">The path to a patch or patch XML file.</param>
        /// <param name="validatePatch">If true, validates that the patch points only to a patch.</param>
        /// <returns>True if the path was added; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null or empty.</exception>
        /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist or is not a file.</exception>
        internal bool Add(string path, bool validatePatch = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }
            else if (!File.Exists(path))
            {
                var message = string.Format(CultureInfo.CurrentCulture, Properties.Resources.Error_InvalidFile, path);
                throw new FileNotFoundException(message, path);
            }

            bool ispatch = IsPatch(path);
            if (!validatePatch || ispatch)
            {
                this.Patches.Add(path);

                // Add the target ProductCodes to the set.
                if (ispatch)
                {
                    this.AddTargetProductCodesFromPatch(path);
                }
                else
                {
                    this.AddTargetProductCodesFromXml(path);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Start to get a list of patches that are applicable to the given ProductCode in a separate thread.
        /// </summary>
        /// <param name="product">The ProductCode for which applicability is determined.</param>
        /// <param name="userSid">The SID of the user for which the product is installed. The default is null.</param>
        /// <param name="context">The <see cref="UserContexts"/> into which the product is installed. This must be <see cref="UserContexts.None"/> for a package path. The default is <see cref="UserContexts.None"/>.</param>
        /// <returns>An <see cref="IAsyncResult"/> you can use to query the current state of the task and return results when complete.</returns>
        /// <exception cref="ArgumentException">The parameters are not correct for the given package path or installed ProductCode (ex: cannot use <see cref="UserContexts.All"/> in any case).</exception>
        internal IAsyncResult BeginGetApplicablePatches(string product, string userSid = null, UserContexts context = UserContexts.None)
        {
            return this.action.BeginInvoke(product, userSid, context, null, null);
        }

        /// <summary>
        /// Gets a list of patches that are applicable to the given ProductCode.
        /// </summary>
        /// <param name="product">The ProductCode for which applicability is determined.</param>
        /// <param name="userSid">The SID of the user for which the product is installed. The default is null.</param>
        /// <param name="context">The <see cref="UserContexts"/> into which the product is installed. This must be <see cref="UserContexts.None"/> for a package path. The default is <see cref="UserContexts.None"/>.</param>
        /// <returns>An ordered list of applicable patch or patch XML files.</returns>
        /// <exception cref="ArgumentException">The parameters are not correct for the given package path or installed ProductCode (ex: cannot use <see cref="UserContexts.All"/> in any case).</exception>
        internal IEnumerable<PatchSequence> GetApplicablePatches(string product, string userSid = null, UserContexts context = UserContexts.None)
        {
            // Enumerate all applicable patches immediately and return.
            return this.DetermineApplicablePatches(product, userSid, context).ToList();
        }

        /// <summary>
        /// Returns the list of patches that are applicable to the given ProductCode.
        /// </summary>
        /// <param name="result">An <see cref="IAsyncResult"/> you can use to return results.</param>
        /// <returns>The list of patches that are applicable to the ProductCode given in <see cref="BeginGetApplicablePatches"/>.</returns>
        internal IEnumerable<PatchSequence> EndGetApplicablePatches(IAsyncResult result)
        {
            return this.action.EndInvoke(result);
        }

        private static bool IsPatch(string path)
        {
            try
            {
                return FileType.Patch == PowerShell.FileInfo.GetFileTypeInternal(path);
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<PatchSequence> DetermineApplicablePatches(string product, string userSid, UserContexts context)
        {
            var patches = this.Patches.ToArray();
            InapplicablePatchHandler handler = (patch, ex) => this.OnInapplicablePatch(new InapplicablePatchEventArgs(patch, product, ex));

            // Keep track of the sequence.
            int i = 0;

            // The current implementation does not return a null list.
            var applicable = Installer.DetermineApplicablePatches(product, patches, handler, userSid, context);
            foreach (var path in applicable)
            {
                yield return new PatchSequence()
                {
                    Patch = path,
                    Sequence = i++,
                    Product = product,
                    UserSid = userSid,
                    UserContext = context,
                };
            }
        }

        private void OnInapplicablePatch(InapplicablePatchEventArgs args)
        {
            var handler = this.InapplicablePatch;
            if (null != handler)
            {
                handler(this, args);
            }
        }

        private void AddTargetProductCodesFromPatch(string path)
        {
            using (var patch = new PatchPackage(path))
            {
                foreach (var productCode in patch.GetTargetProductCodes())
                {
                    this.TargetProductCodes.Add(productCode);
                }
            }
        }

        private void AddTargetProductCodesFromXml(string path)
        {
            using (var file = System.IO.File.OpenRead(path))
            {
                var doc = new XPathDocument(file);
                var nav = doc.CreateNavigator();

                nav.MoveToChild("MsiPatch", Namespace);
                var itor = nav.SelectChildren("TargetProductCode", Namespace);

                while (itor.MoveNext())
                {
                    this.TargetProductCodes.Add(itor.Current.Value);
                }
            }
        }
    }
}
