import type { DuplicateGroupDto, GroupMode, GroupsPageDto, MatchKind, PhotoDto } from '../../../types';

/** A scanned picture in the phone's library, with what is given in place of the rest. */
export function photo(fileName: string, more: Partial<PhotoDto> = {}): PhotoDto {
  return {
    id: fileName, fileName, sourcePath: `/Photo Library/${fileName}`, folder: '/Photo Library', fileSizeBytes: 12 * 1024 ** 2, width: 4032, height: 3024,
    format: fileName.split('.').pop(), isVideo: false, takenOn: '2023-12-27T23:34:00', placeName: 'Anaconda, Montana, US', set: 'Primary', kept: false, ...more,
  };
}

/** A group: the one that stays and its copies, matched as given. */
export function group(keeper: PhotoDto, copies: PhotoDto[], match: MatchKind = 'samePicture'): DuplicateGroupDto {
  return {
    key: `group-${keeper.fileName}`, keeper, reclaimableBytes: copies.filter(c => !c.kept).reduce((n, c) => n + c.fileSizeBytes, 0),
    members: copies.map(p => ({ photo: p, match, keeperReason: 'Larger file' })),
  };
}

/** What the service says of the groups of one kind, as one list. */
export function groupsPage(mode: GroupMode, items: DuplicateGroupDto[], more: Partial<GroupsPageDto> = {}): GroupsPageDto {
  const removable = mode === 'duplicates' ? items.reduce((n, g) => n + g.members.filter(m => !m.photo.kept).length, 0) : 0;
  return {
    mode, page: 1, pageSize: 200, total: items.length, totalPages: 1, removableCount: removable,
    reclaimableBytes: items.reduce((n, g) => n + g.reclaimableBytes, 0), items, ...more,
  };
}
