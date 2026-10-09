# Place list

`places.tsv.gz` is built into the assembly and read by `GeoNamesPlaceResolver` to name the town
nearest to where a picture was taken. Keeping the list in the application means photo locations are
never sent to an online service.

## Source and licence

The data comes from [GeoNames](https://www.geonames.org/), file `cities1000.zip` (populated places
of 1,000 people or more), with region names from `admin1CodesASCII.txt`. GeoNames data is licensed
under [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/); this file is
an extract of it (five columns, sorted), which that licence permits provided GeoNames is credited.

Downloaded 2026-10-06: 171,151 places.

## Format

Gzip-compressed UTF-8 text, one place per line, tab-separated:

```
name    region    country code    latitude    longitude
Buxton  North Carolina  US  35.2677  -75.5424
```

## Landmarks

`landmarks.tsv.gz` is the second list: sights, parks, beaches and districts, from the GeoNames file
`allCountries.zip` under the same licence. It lets Organize name a folder after the landmark or area the
pictures were taken at. Downloaded 2026-10-08: 363,564 landmarks.

```
name    latitude    longitude    reach in metres
Old Faithful Geyser  44.4605  -110.8281  300
```

GeoNames gives a feature one point however large it is, so each kind is given a short reach (200 m for a
theatre or a square, up to 1 km for a district): within it a picture was taken at the place, not merely
near it. `build_landmarks.py` in this folder holds the kinds that are taken and the reach of each, and
rebuilds the file:

```
python build_landmarks.py allCountries.zip landmarks.tsv.gz
```

## Rebuilding

Download `cities1000.zip` and `admin1CodesASCII.txt` from
<https://download.geonames.org/export/dump/>, keep the columns above (name, the region name for
`country.admin1`, country code, latitude and longitude to four decimals), sort the lines, and gzip
the result to this path.
