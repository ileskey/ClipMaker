# Phase 1 4a-3d-9 bounded standalone presentation references

2026-10-14, 시작 HEAD11da63f, phase/1, Base 유지.

보조 TS/packed-audio VOD leaf parser와 first-reference collector를 구현했다. fMP4 parser 조건을 완화하지 않고 별도 standalone 전략으로 분리했다. EXTINF를 exact rational playlist 구간으로 유지하며 version3~7·ENDLIST·positive target·sequence·URI·상한을 검사한다. map/encryption/byte-range/discontinuity/live/master는 지원하지 않는다. first sequence0/처음 span budget만 수집하며 이를 native/source 원점으로 해석하지 않는다.

leaf body와 first whole segment body를 complete bounded reader로 한 번씩 읽는다. segment4MiB·누적 caller byte/span budget, 고정 전송 timeout 없음+취소를 유지한다. 전체 bytes를 확인한 뒤 CreateNew 소유 파일/hash/actual 보조 format provenance를 반환한다. 소유 파일 실패/취소의 Data=null·close/삭제 불확실 시 remaining 인계를 유지한다. selected137/140을보조270/234로바꾸지않는다.

자동644개 통과(신규15개), 실패/skip0. standalone rational 구간/fMP4 조건 회귀·미지원 layout, first sequence/span/byte budget/transport·HTTP 실패·전송 취소와 사용자 파일 보존을검사했다. 솔루션 build 경고0/오류0. parser 복사 중 잔존 initialization 필드로 발생한 컴파일 오류는 제거하고 최종 검증을 실행했다.

실제 현재 source의 provider master를 재조회/선택재검증한 뒤 보조270/234 각각 첫 segment를 받았다. ignored `.artifacts/presentation-source-real/`의 GUID폴더에파일과safe summary를보존했다.8MiB/12초 실험budget, 선택/metadata변경·설치·cookies·전체영상/fallback없이실행했다.

|보조|first file bytes|누적사용 bytes|
|---|---:|---:|
|270 video|1387064|1457328|
|234 audio|98614|1603808|

누적사용은 master23445bytes 및두leaf body와실제segment를포함한다. 이번성공은선언/leaf/byte완전성/소유파일이며content/native 관계·decoder 준비·SharedAxis·제품target통합 성공이아니다. 동시first파일을publicregistry/bridge생산경로에연결하는작업과전체4a/4단계는남았다. close/delete fault 주입은이새collector에서아직검증하지않았다.
