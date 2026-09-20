/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Infrastructure.FoundBooks.Scanning;
using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Infrastructure.DependencyInjection.FoundBooks;

internal static class FoundBooksRegistrationExtensions
{
    public static IServiceCollection AddFoundBookServices(this IServiceCollection services)
    {
        services.AddScoped<IFoundBookRepository, EfFoundBookRepository>();
        services.AddScoped<IFoundBookProbe, FfprobeFoundBookProbe>();
        services.AddScoped<IFoundBookScanner, FoundBookScanner>();
        services.AddScoped<IFoundBookWatchFolderResolver, FoundBookWatchFolderResolver>();
        services.AddScoped<IFoundBookScanService, FoundBookScanService>();
        services.AddScoped<FoundBookCleanup>();
        services.AddScoped<IFoundBookDecisionService, FoundBookDecisionService>();
        services.AddScoped<IFoundBookMatchService, FoundBookMatchService>();
        services.AddScoped<IFoundBookCatalogueMatcher, FoundBookCatalogueMatcher>();
        services.AddScoped<IFoundBookAutoMatchService, FoundBookAutoMatchService>();
        services.AddScoped<IFoundBookAutoAddService, FoundBookAutoAddService>();
        services.AddScoped<IFoundBookImportRunner, FoundBookImportRunner>();
        services.AddScoped<IFoundBookImportService, FoundBookImportService>();
        services.AddSingleton<IFoundBookImportSignal, FoundBookImportSignal>();
        // The host that owns a manual-import workflow and a naming service replaces
        // these; a test host keeps them.
        services.TryAddScoped<IFoundBookImporter, UnavailableFoundBookImporter>();
        services.TryAddScoped<ILibraryDestinationPlanner, RootLibraryDestinationPlanner>();
        return services;
    }
}
