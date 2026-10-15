# Phase 1 4a-3d-1 bounded MP4 init/SIDX 직접 참조

실행일: 2026-10-14 (Asia/Seoul). 시작 HEAD `33a4ca7cb44ff8c8a00d02199da35fdfbbb1a7b0`, `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **bounded prefix의 init/SIDX 직접 byte-reference reader 구현·관련 검증 완료**. [HLS target](PHASE_1_STAGE_4A3C7_ACQUIRE_RESULT.md) 뒤 YouTube indexed 경로의 첫 단위다. indexed HTTP adapter/파일 수집·movie/native bridge·분리 A/V 관계·4a 전체 완료는 아니다.

Mp4SidxReader는 byte0부터의 complete bounded prefix와 별도로 확인한 전체 resource length를 입력으로 받는다. 고유 ftyp/moov 초기화 box를 복사하고 partial 파일에 원본 전체 offset을 가리키는 sidx를 넣지 않는다. moof/mdat payload는 할당하지 않고 그 시작에서 prefix 해석을 끝낸다.

- prefix≤2MiB·top-level box≤10,000,32-bit/extended size·parent bounds·고유 init/index를 검사한다. media box 안에서 prefix가 끝날 수 있다. 필요한 init/index가 incomplete이면 실패하며 무한 prefix 확대/fallback은 하지 않는다.
- sidx v0/v1의 positive reference ID/timescale, unsigned32/64 earliest time·first byte offset을 해석한다. index time은 BigInteger 유리수이며 signed long으로 wrap하지 않는다. first byte는index box end+first_offset이다.
- 직접 reference size·duration과 SAP declaration을 보존한다. resource bounds/checked Int64 byte offsets·zero duration/size·count/body length·reserved fields를 검사한다. hierarchical reference/version 밖은 UnsupportedFormat, malformed/overflow/누락/중복은 InvalidMetadata/Data=null이다.
- init bytes는 복사되고 reference 목록은read-only다. index 좌표는 HLS playlist/user/movie/native 좌표와 별개다. SAP flag만으로 decoder 준비/keyframe 성공을 판정하지 않는다.

## 실제 검증

|실행|결과|
|---|---|
|Downloader Infrastructure tests `--no-restore`|exit0,574 통과/0 실패/0 skip|
|`dotnet build ClipMaker.slnx --no-restore`|exit0,경고0/오류0|
|실제 YouTube sFXPM_sfJNo 명시137/140 pair의64KiB prefix Range|양쪽 reader Succeeded|

새11개 사례는 index/byte 좌표 구별·SAP·unsigned64 timestamp·version/hierarchy 거절·resource/offset bounds·zero 값·partial media payload·중복/누락/cap을 검사했다. 새 테스트/빌드 실패는 없다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

기존 G4b-2 폴더에서 원본 sidx를 뺀 partial files는 보존돼 있으나 원시 header prefix를 찾지 못했다. 따라서 과거 숫자 기록을 actual byte parse 성공으로 대신하지 않고 승인된 source를 새 metadata 조회/재검증 후 검사했다. 원격 input137/140은 테스트의명시적 pair이며 selector/출력 fallback을 바꾼 것이 아니다. 각각 exact HeaderRange206의ResourceLength를 확인하고64KiB만 읽었다. 실패 시 query fallback은 하지 않는다. signed URL/header/raw metadata는메모리에서만 사용했고 safe numeric 결과만 보고했다.

|관찰|video137|audio140|
|---|---:|---:|
|prefix bytes|65536|65536|
|resource bytes|53949513|3658786|
|direct references|38|23|
|first start byte|1230|1031|
|first bytes|1337439|162005|
|first index time|[0,6)|[0,22016/2205)|
|SAP declaration|starts/type1/delta0|starts/type1/delta0|

새 원격 영상 전송은각65536bytes prefix만이다. 기존 프로젝트/도구를 재사용하고 설치/cookies/remote components·전체영상 다운로드·사용자 파일 변경은 없다. harness3분 cancellation은실험용이다.

## 다음·커밋

다음은 이 직접참조를byte reader/CreateNew 소유 파일과연결하고 actual offset/complete body/선택 source 변화·취소/잔존자원을검증한다. movie/edit/native와분리 A/V 관계를확인하기전 index0을source O로확정하지 않는다. 현재4a는미완료이며4b/4c·Phase2·WPF다운로드 UI·ClipEditor는진행하지 않는다.

범위: Infrastructure 신규1·test 신규1·결과/설계/source 계약/baseline4문서. 메시지: `feat: Read bounded MP4 initialization and direct SIDX references`.
