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

## Rebuilding

Download `cities1000.zip` and `admin1CodesASCII.txt` from
<https://download.geonames.org/export/dump/>, keep the columns above (name, the region name for
`country.admin1`, country code, latitude and longitude to four decimals), sort the lines, and gzip
the result to this path.
