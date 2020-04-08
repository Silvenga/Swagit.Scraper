using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Swagit.Scraper
{
    public class Crawler
    {
        private readonly CrawlerOptions _options;

        public Crawler(CrawlerOptions options)
        {
            _options = options;
        }

        public async Task<Archive> GetArchive()
        {
            var archiveAddress = new UriBuilder(_options.ArchiveAddress.Scheme, _options.ArchiveAddress.Host, _options.ArchiveAddress.Port).Uri;

            var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader());
            var archivePage = await context.OpenAsync(archiveAddress.ToString());

            return new Archive(context, archivePage, archiveAddress);
        }
    }

    public class Archive
    {
        private readonly IBrowsingContext _context;
        private readonly IDocument _document;

        public string Name { get; }

        public Uri ArchiveAddress { get; }

        public Archive(IBrowsingContext context, IDocument document, Uri archiveAddress)
        {
            _context = context;
            _document = document;
            ArchiveAddress = archiveAddress;
            Name = document.Title
                           .Split(" - ", 2)
                           .LastOrDefault();
        }

        public Task<ArchiveSectionCollection> GetSections()
        {
            return Task.FromResult(new ArchiveSectionCollection(_context, _document, ArchiveAddress));
        }
    }

    public class ArchiveSectionCollection
    {
        private readonly IBrowsingContext _context;
        private readonly IDocument _document;
        private readonly Uri _archiveAddress;

        private IEnumerable<IHtmlAnchorElement> SectionElements => _document
                                                                   .QuerySelectorAll<IHtmlAnchorElement>(".nav-tabs-swagit > li[role=presentation] > a")
                                                                   .Where(x => x.PathName != null)
                                                                   .Where(x => !x.PathName.Equals("/live", StringComparison.OrdinalIgnoreCase));

        public IEnumerable<string> Slugs => SectionElements.Select(GetSlug);

        public ArchiveSectionCollection(IBrowsingContext context, IDocument document, Uri archiveAddress)
        {
            _context = context;
            _document = document;
            _archiveAddress = archiveAddress;
        }

        public async Task<ArchiveSection> GetSection(string slug)
        {
            var navSection = SectionElements.FirstOrDefault(x => string.Equals(x.PathName, $"/{slug}", StringComparison.OrdinalIgnoreCase));
            if (navSection == null)
            {
                return null;
            }

            var name = navSection.Text;
            var address = new Uri($"{_archiveAddress}archive/{slug}");

            var sectionDocument = await _context.OpenAsync(address.ToString());
            var highestPage = sectionDocument.QuerySelectorAll<IHtmlAnchorElement>("div#main div.pagination > a")
                                             .Where(x => int.TryParse(x.Text, out _))
                                             .Select(x => (int?) int.Parse(x.Text))
                                             .OrderByDescending(x => x)
                                             .FirstOrDefault() ?? 1;

            return new ArchiveSection(sectionDocument, _context)
            {
                Name = name,
                Slug = slug,
                SectionAddress = address,
                Pages = highestPage
            };
        }

        private string GetSlug(IHtmlAnchorElement element)
        {
            // /city-council -> city-council
            return element.PathName?.Substring(1);
        }
    }

    public class ArchiveSection
    {
        private readonly IDocument _document;
        private readonly IBrowsingContext _context;

        public string Name { get; set; }

        public string Slug { get; set; }

        public Uri SectionAddress { get; set; }

        public int Pages { get; set; }

        public ArchiveSection(IDocument document, IBrowsingContext context)
        {
            _document = document;
            _context = context;
        }

        public async Task<ICollection<Video>> GetPageVideos(int pageNumber)
        {
            if (pageNumber <= 0)
            {
                pageNumber = 1;
            }

            if (pageNumber > Pages)
            {
                pageNumber = Pages;
            }

            var pageAddress = new UriBuilder(SectionAddress)
            {
                Query = $"?page={pageNumber}"
            }.Uri;

            var pageDocument = await _context.OpenAsync(pageAddress.ToString());

            var videosQuery = pageDocument.QuerySelectorAll<IHtmlTableDataCellElement>("table#video-table td")
                                          .Select(x => new
                                          {
                                              Row = x,
                                              Anchor = x.QuerySelector<IHtmlAnchorElement>("a"),
                                              Date = x.ChildNodes.FirstOrDefault(n => n.NodeName == "#text")?.TextContent,
                                          })
                                          .Where(x => x.Anchor != null)
                                          .Where(x => x.Anchor.Href != null)
                                          .Where(x => x.Date != null);

            var videos = new List<Video>();
            foreach (var videoElements in videosQuery)
            {
                var name = videoElements.Anchor.Text;
                var slug = videoElements.Anchor.PathName?.Substring("/play/".Length);
                var address = new Uri(videoElements.Anchor.Href);

                var video = new Video
                {
                    Name = name,
                    Slug = slug,
                    VideoAddress = address,
                };

                if (DateTime.TryParse(videoElements.Date, out var date))
                {
                    video.Date = date;
                }

                videos.Add(video);
            }

            return videos;
        }
    }

    public class Video
    {
        public string Name { get; set; }

        public string Slug { get; set; }

        public Uri VideoAddress { get; set; }

        public DateTime? Date { get; set; }

        public async Task GetSegments()
        {
            throw new NotImplementedException();
        }
    }
}