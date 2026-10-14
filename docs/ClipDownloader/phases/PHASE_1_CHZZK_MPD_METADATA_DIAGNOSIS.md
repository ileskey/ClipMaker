# Phase 1 후속 — CHZZK DASH 메타데이터 추출 실패

2026-10-15 (Asia/Seoul), branch `phase/1`, 진단 시작 HEAD `e84e1d8`. 사용자가 CHZZK14327648의 Fast10~40초와10:10~10:40 요청 모두에서 Metadata 단계 `ProcessFailed/Execution`을 보고했다. 다운로드나 구간 처리 전 실패이며 YouTube Fast/Accurate 사용자 재생 통과와 구별한다.

## 실제 재현과 원인

설치된 yt-dlp 버전은2026.08.19다. 다운로드 없이 제품과 동일한 metadata 인자(ignore-config/no-plugin-dirs/no-cache-dir/no-remote-components/no-js-runtimes/simulate/skip-download/dump-single-json/no-playlist/ignore-no-formats-error 및 기존 retry 제한)로 조회해 exit1·`KeyError('sourceURL')`를 재현했다. 별도 verbose 실행도 exit1이며 원문을 저장하지 않고 traceback의 파일/함수 위치만 관찰했다.

실제 traceback은 chzzk `_real_extract` → common `_extract_mpd_formats_and_subtitles` → `_merge_mpd_periods` → `_parse_mpd_periods` → `extract_multisegment_info` → `extract_Initialization` → KeyError였다. 제품은 비정상 도구 종료를 기존 안전한 일반 오류로 반환하고 raw stderr를 복제하지 않는다.

CHZZK `/service/v3/videos/14327648`의 실제 응답 code200, vodStatus ABR_HLS, duration42980초, videoId/inKey 존재, liveRewindPlaybackJson 없음이었다. 키 값을 출력하지 않고 같은 Naver playback endpoint를 조회했다. application/dash+xml 응답의 root는MPD, Representation7개·SegmentList1개·SegmentBase0개·Initialization1개다. 그 Initialization의 속성 이름은range 하나이며 sourceURL이 없다. XML·signed URL·키 값은 저장하거나 출력하지 않았다.

[yt-dlp CHZZK extractor](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/yt_dlp/extractor/chzzk.py)는 ABR_HLS 응답에서 Naver MPD를 추출한다. [공통 MPD parser](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/yt_dlp/extractor/common.py)는 Initialization의 sourceURL 속성을 직접 참조한다. 실제 응답의 속성 누락과 traceback이 대응하므로 **이번 조회 실패는 추출기의 MPD 처리와 실제 응답 구조 사이의 불일치**로 확인했다. ABR_HLS라는 API 상태 문자열을 HLS playlist 지원 증거로 해석하지 않는다.

조회 시점 [upstream master](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/common.py)에도 같은 직접 접근이 남아 있었다. 설치 파일과 공개 소스의 줄 번호는 달랐지만 함수/직접 속성 접근 관계는 일치했다. 다른 release 설치·실행은 하지 않았으며 업데이트로 해결된다고 확정하지 않는다. 삭제·비공개·인증 필수 또는 표준 위반 manifest라고도 판정하지 않는다.

## 지원 범위와 잔여 작업

현재 metadata parser는 https를Https, m3u8/m3u8_native를Hls, 그 밖의 protocol을Other로 분류한다. source acquisition의 구현·검증 전략은 HLS TS와 분리 HTTPS indexed MP4다. 이번 MPD의 최종 yt-dlp format 목록은 추출 실패로 얻지 못했으며, Initialization 처리만 보완하면 기존 indexed 전략을 사용할 수 있다고 추정하지 않는다.

이 입력 지원에는 MPD 추출 보완 뒤 실제 선택 format/transport와 초기화 byte range·segment 시간·원점·codec/native 관계를 먼저 확인해야 한다. 기존 전략에 정확히 대응하는지 입증하거나 별도 제한 MPD 전략을 설계·실험해야 한다. range/initialization·segment를 임의 누락하거나 전체 다운로드로 우회하지 않는다. 이번에는 제품 코드·yt-dlp 설치·도구 교체·영상 다운로드·외부 이슈 게시를 하지 않았다.

이 영상 생성 지원은 **미완료**다. 시간 변경·Fast/Accurate 변경은 metadata 단계의 이 실패를 해결하지 않는다. CHZZK 양 모드의 사용자 player 검증도 남아 있고 Phase1 전체 Passed/Completion/Tag는Pending이다. 기존 성공한 CHZZK HLS 사례 전체가 실패했다는 뜻으로 확대하지 않는다. 검증된 HLS 사례로 player 확인을 진행하는 것과 이 DASH source의 새 지원을 구별한다.

안전한 구조화 결과는 [JSON](evidence/PHASE_1_CHZZK_MPD_METADATA_DIAGNOSIS.json)에 기록한다. 문서 변경만 했으므로 제품 build/test를 재실행하지 않았고 마지막 제품 검증은1192개 통과·build 경고/오류0이다. 사용자 미추적 영상은 그대로 보존한다.
