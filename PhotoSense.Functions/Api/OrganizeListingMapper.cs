using System.Globalization;
using PhotoSense.Application.Organizing;
using PhotoSense.Contracts.Organize;
using PhotoSense.Domain.Services;

namespace PhotoSense.Functions.Api;

/// <summary>
/// Turns the files of a folder into what the Organize page shows: each with the place it was taken at,
/// the places themselves listed once. A place is a landmark where the picture was taken at one, and
/// otherwise the nearest town.
/// </summary>
public sealed class OrganizeListingMapper
{
    /// <summary>The most files handed over in one answer about a folder that is still being read.</summary>
    public const int BatchSize = 4000;
    private const string PlacesNote = "places";
    private readonly PlaceLookup _places;

    public OrganizeListingMapper(PlaceLookup places) => _places = places;

    public static string Wall(DateTime clock) => clock.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The whole listing of a folder, once it has been read.</summary>
    public OrganizeFilesDto Whole(string root, IReadOnlyList<LibraryFile> files)
    {
        var places = new PlaceList();
        var listed = files.Select(f => Map(f, places)).ToList();
        return new OrganizeFilesDto { Root = root, FromScan = files.All(f => f.FromScan), Places = places.From(0), Files = listed };
    }

    /// <summary>
    /// What has been read of a folder so far, for a page that already has some of it: the files it has not
    /// had yet, and the places it has not had yet. Places keep their numbers from one answer to the next
    /// for as long as the same reading goes on.
    /// </summary>
    /// <param name="progress">How far the reading has got, with the files to hand over; null when the folder is not being read.</param>
    /// <param name="placesFrom">How many of the reading's places the page already has.</param>
    public OrganizeProgressDto Partial(ListingProgress? progress, int placesFrom)
    {
        if (progress is null) return new OrganizeProgressDto();
        var places = (PlaceList)progress.Notes.GetOrAdd(PlacesNote, _ => new PlaceList());
        var files = progress.Found.Select(f => Map(f, places)).ToList();
        return new OrganizeProgressDto
        {
            Reading = true, ReadingId = progress.Reading.ToString("N"), Total = progress.Total, Done = progress.Done,
            From = progress.From, More = progress.More, Files = files, Places = places.From(placesFrom),
        };
    }

    private OrganizeFileDto Map(LibraryFile f, PlaceList places) => new()
    {
        Id = f.Id, Name = f.Name, Folder = f.Folder, SizeBytes = f.SizeBytes, IsVideo = f.IsVideo, Date = Wall(f.Date), DateFromFile = f.DateFromFile,
        Width = f.Details.Width, Height = f.Details.Height, DurationSeconds = f.Details.DurationSeconds,
        Place = places.NumberOf(_places.At(f.Details.Latitude, f.Details.Longitude)),
    };

    // The places of one listing, each given a number the first time a file turns out to have been taken there.
    private sealed class PlaceList
    {
        private readonly List<OrganizePlaceDto> _places = [];
        private readonly Dictionary<(string, string, string, string?), int> _numbers = [];

        public int? NumberOf(PlaceMatch? match)
        {
            if (match is null) return null;
            var key = (match.Country, match.Region, match.Town, match.Landmark);
            // Several pages may be asking about one reading at the same moment.
            lock (_places)
            {
                if (_numbers.TryGetValue(key, out var number)) return number;
                _places.Add(new OrganizePlaceDto
                {
                    Town = match.Town, State = match.Region.Length > 0 ? match.Region : match.Country, Country = match.Country, Area = match.Landmark,
                    Latitude = match.LandmarkLatitude ?? match.Latitude, Longitude = match.LandmarkLongitude ?? match.Longitude,
                });
                return _numbers[key] = _places.Count - 1;
            }
        }

        public List<OrganizePlaceDto> From(int first)
        {
            lock (_places) return _places.Skip(Math.Max(0, first)).ToList();
        }
    }
}
