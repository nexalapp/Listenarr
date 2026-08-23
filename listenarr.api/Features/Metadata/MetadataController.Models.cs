/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Metadata
{
    public partial class MetadataController : ControllerBase
    {
        public sealed class AuthorLookupResponse
        {
            public string? Asin { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Image { get; set; }
            public string? CachedPath { get; set; }
            public string? Description { get; set; }
            public List<RelatedAuthorItem> SimilarAuthors { get; set; } = new();
        }

        public sealed class AuthorLookupRefreshRequest
        {
            public string Name { get; set; } = string.Empty;
            public string Region { get; set; } = "us";
            public string? Asin { get; set; }
        }

        public sealed class RelatedAuthorItem
        {
            public string? Asin { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public sealed class SeriesLookupResponse
        {
            public string? Asin { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Image { get; set; }
            public string? CachedPath { get; set; }
            public string? Description { get; set; }
            public int TotalBooks { get; set; }
        }

        public sealed class SeriesLookupRefreshRequest
        {
            public string Name { get; set; } = string.Empty;
            public string Region { get; set; } = "us";
            public string? Asin { get; set; }
        }

        public sealed class AuthorCatalogResponse
        {
            public AuthorCatalogAuthorInfo Author { get; set; } = new();
            public List<AuthorCatalogBookItem> Books { get; set; } = new();
            public int TotalBooks { get; set; }
        }

        public sealed class CatalogRefreshRequest
        {
            public string Name { get; set; } = string.Empty;
            public string Region { get; set; } = "us";
            public int Limit { get; set; } = 250;
        }

        public sealed class AuthorCatalogAuthorInfo
        {
            public string? Asin { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Image { get; set; }
        }

        public sealed class AuthorCatalogSeriesMembership
        {
            public string? Name { get; set; }

            public string? Position { get; set; }
        }

        public sealed class AuthorCatalogBookItem
        {
            public string? Asin { get; set; }
            public string Title { get; set; } = string.Empty;
            public string? Subtitle { get; set; }
            public List<string> Authors { get; set; } = new();
            public string? ImageUrl { get; set; }
            public int? Runtime { get; set; }
            public string? Language { get; set; }
            public string? Publisher { get; set; }
            public List<string> Narrators { get; set; } = new();
            public List<string> Genres { get; set; } = new();
            public string? Series { get; set; }
            public string? SeriesNumber { get; set; }

            /// <summary>
            /// Every series this book belongs to, not just the first.
            /// </summary>
            /// <remarks>
            /// Audible lists a book under more than one series - the Harry Potter novels
            /// appear under both "Harry Potter" and "Wizarding World Collection". Collapsing
            /// to the first membership splits a series across groups and makes each one look
            /// incomplete, and can pair one series' name with another's position.
            /// Series and SeriesNumber are retained as the primary membership.
            /// </remarks>
            public List<AuthorCatalogSeriesMembership> SeriesMemberships { get; set; } = new();
            public string? PublishedDate { get; set; }
            public string? Isbn { get; set; }
            public string? Link { get; set; }
            public string? MetadataSource { get; set; }
        }

        public sealed class SeriesCatalogResponse
        {
            public SeriesCatalogInfo Series { get; set; } = new();
            public List<SeriesCatalogBookItem> Books { get; set; } = new();
            public int TotalBooks { get; set; }
        }

        public sealed class SeriesCatalogInfo
        {
            public string? Asin { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Image { get; set; }
            public string? Description { get; set; }
        }

        public sealed class SeriesCatalogBookItem
        {
            public string? Asin { get; set; }
            public string Title { get; set; } = string.Empty;
            public string? Subtitle { get; set; }
            public List<string> Authors { get; set; } = new();
            public string? ImageUrl { get; set; }
            public int? Runtime { get; set; }
            public string? Language { get; set; }
            public string? Publisher { get; set; }
            public List<string> Narrators { get; set; } = new();
            public List<string> Genres { get; set; } = new();
            public string? Series { get; set; }
            public string? SeriesNumber { get; set; }
            public string? PublishedDate { get; set; }
            public string? Isbn { get; set; }
            public string? Link { get; set; }
            public string? MetadataSource { get; set; }
        }
    }
}
