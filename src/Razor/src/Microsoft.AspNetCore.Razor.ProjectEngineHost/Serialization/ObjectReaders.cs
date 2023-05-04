﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Razor.Language;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Razor.ProjectEngineHost.Serialization;

internal static partial class ObjectReaders
{
    private static readonly StringCache s_stringCache = new();

    [return: NotNullIfNotNull(nameof(str))]
    private static string? Cached(string? str)
    {
        if (str is null)
        {
            return null;
        }

        // Some of the strings used in TagHelperDescriptors are interned by other processes,
        // so we should avoid duplicating those.
        var interned = string.IsInterned(str);
        if (interned != null)
        {
            return interned;
        }

        // We cache all our stings here to prevent them from balooning memory in our Descriptors.
        return s_stringCache.GetOrAddValue(str);
    }

    public static RazorExtension ReadExtension(JsonReader reader)
        => reader.ReadNonNullObject(ReadExtensionFromProperties);

    public static RazorExtension ReadExtensionFromProperties(JsonReader reader)
    {
        var extensionName = reader.ReadNonNullString(nameof(RazorExtension.ExtensionName));

        return new SerializedRazorExtension(extensionName);
    }

    public static RazorConfiguration ReadConfigurationFromProperties(JsonReader reader)
    {
        ConfigurationData data = default;
        reader.ReadProperties(ref data, ConfigurationData.PropertyMap);

        return RazorConfiguration.Create(data.LanguageVersion, data.ConfigurationName, data.Extensions);
    }

    public static RazorDiagnostic ReadDiagnostic(JsonReader reader)
        => reader.ReadNonNullObject(ReadDiagnosticFromProperties);

    public static RazorDiagnostic ReadDiagnosticFromProperties(JsonReader reader)
    {
        DiagnosticData data = default;
        reader.ReadProperties(ref data, DiagnosticData.PropertyMap);

        var descriptor = new RazorDiagnosticDescriptor(data.Id, MessageFormat(data.Message), data.Severity);

        return RazorDiagnostic.Create(descriptor, data.Span);

        static Func<string> MessageFormat(string message)
        {
            return () => message;
        }
    }

    public static TagHelperDescriptor ReadTagHelper(JsonReader reader, bool useCache)
        => reader.ReadNonNullObject(reader => ReadTagHelperFromProperties(reader, useCache));

    public static TagHelperDescriptor ReadTagHelperFromProperties(JsonReader reader, bool useCache)
    {
        // Try reading the optional hashcode
        var hashWasRead = reader.TryReadInt32(RazorSerializationConstants.HashCodePropertyName, out var hash);
        if (useCache && hashWasRead &&
            TagHelperDescriptorCache.TryGetDescriptor(hash, out var descriptor))
        {
            reader.ReadToEndOfCurrentObject();
            return descriptor;
        }

        // Required tokens (order matters)
        if (!reader.TryReadString(nameof(TagHelperDescriptor.Kind), out var descriptorKind))
        {
            return default!;
        }

        if (!reader.TryReadString(nameof(TagHelperDescriptor.Name), out var typeName))
        {
            return default!;
        }

        if (!reader.TryReadString(nameof(TagHelperDescriptor.AssemblyName), out var assemblyName))
        {
            return default!;
        }

        using var _ = TagHelperDescriptorBuilder.GetPooledInstance(
            Cached(descriptorKind), Cached(typeName), Cached(assemblyName),
            out var builder);

        reader.ProcessProperties(new TagHelperReader(builder), TagHelperReader.PropertyMap);

        descriptor = builder.Build();

        if (useCache && hashWasRead)
        {
            TagHelperDescriptorCache.Set(hash, descriptor);
        }

        return descriptor;
    }

    private static void ProcessDiagnostic(JsonReader reader, RazorDiagnosticCollection collection)
    {
        DiagnosticData data = default;
        reader.ReadObjectData(ref data, DiagnosticData.PropertyMap);

        var descriptor = new RazorDiagnosticDescriptor(Cached(data.Id), MessageFormat(data.Message), data.Severity);
        var diagnostic = RazorDiagnostic.Create(descriptor, data.Span);

        collection.Add(diagnostic);

        static Func<string> MessageFormat(string message)
        {
            return () => Cached(message);
        }
    }

    private static void ProcessMetadata(JsonReader reader, IDictionary<string, string?> dictionary)
    {
        while (reader.TryReadNextPropertyName(out var key))
        {
            var value = reader.ReadString();
            dictionary[key] = value;
        }
    }
}
