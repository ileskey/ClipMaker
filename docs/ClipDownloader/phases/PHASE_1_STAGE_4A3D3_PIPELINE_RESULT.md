# Phase 1 4a-3d-3 indexed initial pipeline

2026-10-14, 시작 HEAD `6a0d9d0b07e0f2b22a9fb2e6552e9c9d7f8978c3`, clean phase/1. Base 유지.

bounded prefix→SIDX→first whole byte reference→CreateNew→local hash/probe를 연결했다. prefix는 64KiB HeaderRange 한 번이며 resource length와 index를 같은 ephemeral source 객체에 보존한다. prefix 소비량을 초기/target 수집 budget에 포함한다. 실패/취소에서는 Data=null, probe 시작 후 소유 파일은 RemainingFiles에 남기며 termination 미확인을 취소로 숨기지 않는다. 외부 probe가 다른 collected reference를 반환하면 거절한다.

CollectedSourceReference는 local file/byte/hash identity만 공통화했다. HLS playlist와 indexed coordinate는 파생 타입에 별도로 유지한다. 기존 HLS public adapter는 명시 HLS 타입 gate를 유지한다. public YouTube source/native/shared axis 발행은 아직 없다.

검증: Downloader581→586개 통과(0 실패/skip), solution build 경고/오류0. 새 5개 pipeline 사례는 complete prefix/참조 흐름, foreign identity 거절, probe 실패·취소·termination에서 소유 파일 인계를 검사한다. 첫 컴파일의 제네릭 class constraint 누락을 수정한 후 통과했다.

실제 승인된 YouTube sFXPM_sfJNo 명시137/140 pair를 재조회/선택 재검증 후 prefix 각65536bytes와 첫 참조만 받았다. 설치/cookies/전체 다운로드/fallback은 없다. ignored `.artifacts/indexed-source-real/`의 생성 GUID 폴더에 파일과 safe summary를 보존했다. signed URI/header는 메모리에서만 사용했다.

|원본|수집 파일 bytes|local probe|packet/frame|native evidence|
|---|---:|---|---|---|
|137|1338181|Succeeded|180/180|Succeeded|
|140|162728|Succeeded|430/430|UnsupportedFormat|

audio 실패는 현재 2-byte ASC만 지원하는 native gate에서 추가 확인이 필요한 사례다. probe 성공을 native 성공으로 대체하지 않는다. movie edit/분리 관계/target/public 통합·전체4a 및4단계는 미완료다. reflection harness의 PSObject→String 오류는 수집 호출 전에 발생했으며 명시 string 변환 후 실제 검증을 재실행했다. Core/Media.Infrastructure 전체 테스트는 이 단위에서 재실행하지 않았다.

최신 사용자 지시로 이후 개별 커밋 보고를 생략하고 4단계 완료 후 통합 보고한다. 커밋/검증 단위와 문서 증거 기록은 유지한다. 기존 readiness 문서의 '커밋 지점에서 보고'는 현재 지시로 대체된다.
