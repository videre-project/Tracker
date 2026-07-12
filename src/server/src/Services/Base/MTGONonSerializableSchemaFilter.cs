/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using Microsoft.OpenApi;

using MTGOSDK.Core.Reflection.Serialization;

using Swashbuckle.AspNetCore.SwaggerGen;


namespace Tracker.Services.Base;

/// <summary>
/// Aligns Tracker's OpenAPI schemas with MTGOSDK's runtime NonSerializableAttribute
/// behavior for nested properties.
/// </summary>
internal sealed class MTGONonSerializableSchemaFilter : ISchemaFilter
{
  public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
  {
    if (schema.Properties == null || schema.Properties.Count == 0)
    {
      return;
    }

    foreach (var directive in
      NonSerializableJsonContract.GetPropertyDirectives(context.Type))
    {
      switch (directive.Action)
      {
        case NonSerializableJsonPropertyAction.Remove:
          RemoveProperties(schema, directive.PropertyNames);
          break;
        case NonSerializableJsonPropertyAction.Stringify:
          StringifyProperties(schema, directive.PropertyNames);
          break;
        case NonSerializableJsonPropertyAction.StringifyEnumerable:
          StringifyEnumerableProperties(schema, directive.PropertyNames);
          break;
      }
    }
  }

  private static void StringifyProperties(
    IOpenApiSchema schema,
    IEnumerable<string> propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      if (schema.Properties?.ContainsKey(propertyName) == true)
      {
        schema.Properties[propertyName] = new OpenApiSchema
        {
          Type = JsonSchemaType.String,
        };
      }
    }
  }

  private static void StringifyEnumerableProperties(
    IOpenApiSchema schema,
    IEnumerable<string> propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      if (schema.Properties?.ContainsKey(propertyName) == true)
      {
        schema.Properties[propertyName] = new OpenApiSchema
        {
          Type = JsonSchemaType.Array,
          Items = new OpenApiSchema
          {
            Type = JsonSchemaType.String,
          },
        };
      }
    }
  }

  private static void RemoveProperties(
    IOpenApiSchema schema,
    IEnumerable<string> propertyNames)
  {
    if (schema.Properties == null)
    {
      return;
    }

    foreach (var propertyName in propertyNames)
    {
      schema.Properties.Remove(propertyName);
      schema.Required?.Remove(propertyName);
    }
  }
}
