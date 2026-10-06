import type { DuplicateGroupDto, GroupMemberDto, GroupMode, GroupsPageDto, MatchKind, PhotoDto } from '../types';

/** A picture as the server describes it; tests override only what they are about. */
export function photo(overrides: Partial<PhotoDto> = {}): PhotoDto {
  return {
    id: 'p1',
    fileName: 'IMG_4198.JPG',
    sourcePath: 'C:\\photos\\2024\\IMG_4198.JPG',
    folder: 'C:\\photos\\2024',
    fileSizeBytes: 6_093_000,
    width: 4032,
    height: 3024,
    format: 'JPEG',
    isVideo: false,
    set: 'Primary',
    kept: false,
    ...overrides,
  };
}

export function video(overrides: Partial<PhotoDto> = {}): PhotoDto {
  return photo({ id: 'v1', fileName: 'IMG_0042.MOV', format: 'MOV', isVideo: true, width: 1920, height: 1080, durationSeconds: 84, ...overrides });
}

export function member(photoOverrides: Partial<PhotoDto> = {}, match: MatchKind = 'identical', keeperReason = 'Same quality; kept the one with the plainer name'): GroupMemberDto {
  return { photo: photo({ id: 'm1', fileName: 'IMG_4198 (1).JPG', ...photoOverrides }), match, keeperReason };
}

export function group(overrides: Partial<DuplicateGroupDto> = {}): DuplicateGroupDto {
  return { key: 'g1', keeper: photo(), members: [member()], reclaimableBytes: 6_093_000, ...overrides };
}

export function groupsPage(items: DuplicateGroupDto[], overrides: Partial<GroupsPageDto> = {}, mode: GroupMode = 'duplicates'): GroupsPageDto {
  return {
    mode,
    page: 1,
    pageSize: 50,
    total: items.length,
    totalPages: 1,
    removableCount: items.reduce((n, g) => n + g.members.filter(m => !m.photo.kept).length, 0),
    reclaimableBytes: items.reduce((n, g) => n + g.reclaimableBytes, 0),
    items,
    ...overrides,
  };
}

/** A response as fetch resolves it, for standing in for the server. */
export function reply(body?: unknown, status = 200): Response {
  if (body === undefined) return new Response(null, { status });
  return new Response(typeof body === 'string' ? body : JSON.stringify(body), { status });
}
