/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.AbookLink;
using Listenarr.Infrastructure.Search.Providers.AbookLink;
using Listenarr.Infrastructure.Search.Nzb;
using Listenarr.Infrastructure.Search.NzbKing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.DependencyInjection.Search;

internal static class SearchRegistrationExtensions
{
    public static IServiceCollection AddSearchServices(this IServiceCollection services)
    {
        services.AddScoped<IIndexerSearchProvider, InternetArchiveSearchProvider>();
        services.AddScoped<IIndexerSearchProvider, TorznabNewznabSearchProvider>();
        services.AddScoped<IIndexerSearchProvider, MyAnonamouseSearchProvider>();
        services.AddScoped<IIndexerSearchProvider, AbookLinkSearchProvider>();
        services.AddScoped<IMyAnonamouseConnectionTester, MyAnonamouseConnectionTester>();
        services.AddScoped<IndexerAdditionalSettingsParser>();
        services.AddScoped<IndexerSearchWorkflow>();
        services.AddScoped<MetadataSourceCatalog>();
        services.AddScoped<SearchFinalDispositionLogger>();
        services.AddScoped<ISearchService, SearchService>();
        return services;
    }

    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IIndexerRepository, EfIndexerRepository>();
        services.AddScoped<INzbKingLedgerRepository, EfNzbKingLedgerRepository>();
        services.AddScoped<INzbKingTokenBudget, NzbKingTokenBudget>();
        services.AddScoped<NzbKingApiClient>();

        // Free indexes first; the metered one is only reached when they have nothing.
        services.AddScoped<INzbResolver, NzbIndexResolver>();
        services.AddScoped<INzbResolver, BinsearchResolver>();
        services.AddScoped<INzbResolver, NzbKingResolver>();
        services.AddScoped<INzbResolverChain, NzbResolverChain>();

        services.AddScoped<AbookLinkClient>();
        // Its own client: the session cookie is issued on the login redirect, and a
        // client that follows redirects discards those headers before they can be read.
        services.AddHttpClient<AbookLinkSession>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                UseProxy = false
            });
        services.AddScoped<IAbookLinkBrowser, AbookLinkBrowser>();
        services.AddScoped<IAbookGrabResolver, AbookGrabResolver>();
        services.AddScoped<AbookDownloadDispatcher>();
        services.AddScoped<IAbookGrabDispatcher, AbookGrabDispatcher>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
