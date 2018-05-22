using System.IO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Descriptor;
using OrchardCore.Environment.Shell.Descriptor.Models;
using OrchardCore.Environment.Shell.Descriptor.Settings;
using OrchardCore.Modules;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ModularServicesBuilderExtensions
    {
        /// <summary>
        /// Registers a default tenant with a set of features that are used to setup and configure the actual tenants.
        /// For instance you can use this to add a custom Setup module.
        /// </summary>
        public static ModularServicesBuilder WithDefaultFeatures(
            this ModularServicesBuilder builder, params string[] featureIds)
        {
            foreach (var featureId in featureIds)
            {
                builder.Services.AddTransient(sp => new ShellFeature(featureId));
            }

            return builder;
        }

        /// <summary>
        /// Registers tenants defined in configuration.
        /// </summary>
        public static ModularServicesBuilder WithTenants(this ModularServicesBuilder builder)
        {
            builder.Services.AddSingleton<IShellSettingsConfigurationProvider, FileShellSettingsConfigurationProvider>();
            builder.Services.AddScoped<IShellDescriptorManager, FileShellDescriptorManager>();
            builder.Services.AddSingleton<IShellSettingsManager, ShellSettingsManager>();
            builder.Services.AddScoped<ShellSettingsWithTenants>();

            return builder;
        }

        /// <summary>
        /// Registers a single tenant with the specified set of features.
        /// </summary>
        public static ModularServicesBuilder WithFeatures(
            this ModularServicesBuilder builder, params string[] featureIds)
        {
            builder.WithDefaultFeatures(featureIds);
            builder.Services.AddSetFeaturesDescriptor();

            return builder;
        }

        /// <summary>
        /// Adds host and tenant level authentication services.
        /// </summary>
        public static ModularServicesBuilder AddAuthentication(this ModularServicesBuilder builder)
        {
            builder.Services.AddAuthentication();

            builder.Services.ConfigureTenantServices<ShellSettings>((collection, settings) =>
            {
                collection.AddAuthentication();

                // Note: IAuthenticationSchemeProvider is already registered at the host level.
                // We need to register it again so it is taken into account at the tenant level.
                collection.AddSingleton<IAuthenticationSchemeProvider, AuthenticationSchemeProvider>();
            })

            .ConfigureTenant((app, routes, sp) =>
            {
                app.UseAuthentication();
            });

            return builder;
        }

        /// <summary>
        /// Adds host and tenant level antiforgery services.
        /// </summary>
        public static ModularServicesBuilder AddAntiForgery(this ModularServicesBuilder builder)
        {
            builder.Services.AddAntiforgery();

            builder.Services.ConfigureTenantServices<ShellSettings>((collection, settings) =>
            {
                var tenantName = settings.Name;
                var tenantPrefix = "/" + settings.RequestUrlPrefix;

                collection.AddAntiforgery(options =>
                {
                    options.Cookie.Name = "orchantiforgery_" + tenantName;
                    options.Cookie.Path = tenantPrefix;
                });
            });

            return builder;
        }

        /// <summary>
        /// Adds tenant level data protection services.
        /// </summary>
        public static ModularServicesBuilder AddDataProtection(this ModularServicesBuilder builder)
        {
            builder.Services.ConfigureTenantServices<IOptions<ShellOptions>, ShellSettings>((collection, options, settings) =>
            {
                var directory = Directory.CreateDirectory(Path.Combine(
                options.Value.ShellsApplicationDataPath,
                options.Value.ShellsContainerName,
                settings.Name, "DataProtection-Keys"));

                // Re-register the data protection services to be tenant-aware so that modules that internally
                // rely on IDataProtector/IDataProtectionProvider automatically get an isolated instance that
                // manages its own key ring and doesn't allow decrypting payloads encrypted by another tenant.
                // By default, the key ring is stored in the tenant directory of the configured App_Data path.
                collection.Add(new ServiceCollection()
                    .AddDataProtection()
                    .PersistKeysToFileSystem(directory)
                    .SetApplicationName(settings.Name)
                    .Services);
            });

            return builder;
        }
    }
}
