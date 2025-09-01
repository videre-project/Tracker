/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json;


namespace Tracker;

public static class SerializationPolicies
{
  public class CapitalizedCamelCaseNamingPolicy : JsonNamingPolicy
  {
    public override string ConvertName(string name)
    {
      if (string.IsNullOrEmpty(name)) return name;
      // Convert to camelCase first, then capitalize the first letter.
      var camel = char.ToLowerInvariant(name[0]) + name.Substring(1);
      return char.ToUpperInvariant(camel[0]) + camel.Substring(1);
    }
  }
}
