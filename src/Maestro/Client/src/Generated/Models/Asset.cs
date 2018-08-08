// <auto-generated>
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// </auto-generated>

namespace Microsoft.DotNet.Maestro.Client.Models
{
    using Newtonsoft.Json;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public partial class Asset
    {
        /// <summary>
        /// Initializes a new instance of the Asset class.
        /// </summary>
        public Asset()
        {
            CustomInit();
        }

        /// <summary>
        /// Initializes a new instance of the Asset class.
        /// </summary>
        public Asset(int? id = default(int?), string name = default(string), string version = default(string), IList<AssetLocation> locations = default(IList<AssetLocation>))
        {
            Id = id;
            Name = name;
            Version = version;
            Locations = locations;
            CustomInit();
        }

        /// <summary>
        /// An initialization method that performs custom operations like setting defaults
        /// </summary>
        partial void CustomInit();

        /// <summary>
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public int? Id { get; set; }

        /// <summary>
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// </summary>
        [JsonProperty(PropertyName = "version")]
        public string Version { get; set; }

        /// <summary>
        /// </summary>
        [JsonProperty(PropertyName = "locations")]
        public IList<AssetLocation> Locations { get; set; }

    }
}
