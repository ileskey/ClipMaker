# Phase 1 후속 — CHZZK Accurate playlist·위치 제한 진단

2026-10-15 (Asia/Seoul), branch `phase/1`, 시작 HEAD `ac28755`. 사용자15578184·15683602의 Accurate46:10~46:40 오류를 서로 구분했다. 직전14791589 Fast `out6.mp4`의 사용자 재생·음성 동기 통과도 인계/6단계 문서에 기록했다.

## 메타데이터 실패15578184

설치된 yt-dlp2026.08.19에 제품과 같은 metadata 인자를 전달하고 stdout을 버려 영상 다운로드 없이 재현했다. exit1·`KeyError('sourceURL')`였다. [앞선14327648 진단](PHASE_1_CHZZK_MPD_METADATA_DIAGNOSIS.md)과 같은 오류 서명이지만, 이번15578184의 API/MPD 구조를 별도로 검사하지 않았으므로 두 manifest가 완전히 같다고 확정하지 않는다. metadata 실패를 mode/요청 위치로 해결하거나 영상 생성 성공으로 표현하지 않는다.

## playlist 실패15683602

실제 제품 metadata/선택/resolver와 bounded reader를 사용해 Accurate snapshot의best hls-8384 playlist만 조회했다. 기존2MiB 상한에서 reader의 실패는SizeLimitExceeded, HTTP200이었다. 별도 redirect/cookie 없는 identity GET의 응답 headers를 확인했으며 Content-Length2213866 bytes, encoding 없음, HTTP200이다. **playlist body가2097152 bytes의 별도 검사 상한을 넘었다.** media segment는 다운로드하지 않았고 signed URI/headers/metadata/playlist 원문은 저장·출력하지 않았다.

64MiB/60초는 source 참조의 누적 획득 예산이다. playlist 단일 검사2MiB 상한은 별개이므로 이 영상은 원본 시작/요청 구간 획득 전에 차단된다. parser의20000 segment 제한이나codec·clock·출력 검사 통과 여부는 이 실행에서 확인하지 못했다.

`HlsSelectedPlaylistReader`가 byte 초과·HTTP 응답 조건 실패·전송 실패를 같은 미확인 오류로 숨기는 표시를 보완했다. byte 초과는 기존VerificationFailed code를 유지하고 `HlsPlaylistByteBudget`과 적용 상한을 표시한다. 응답 조건은 `HlsPlaylistHttpResponse`, 전송 실패는 기존ProcessFailed와 `HlsPlaylistHttpTransport`, 비HLS transport는 `HlsPlaylistTransportUnsupported`로 구분한다. raw URI/외부 오류 내용은 반환하지 않으며 취소·parser 오류·retry 없음과 기존2MiB 상한은 유지한다. 지원 범위를 확대하거나 이 영상 생성 문제를 해결한 변경은 아니다.

수정 뒤 같은metadata/선택/resolver로실제playlist reader를재실행해 `HlsPlaylistByteBudget`과2097152byte 상한메시지를확인했다. HTTP200·Content-Length2213866은다시관찰됐으며playlist실패뒤media다운로드는없다. 이번재검증은제품reader경로이고최종CLI생성성공을의미하지않는다. [구조화 증거](evidence/PHASE_1_CHZZK_ACCURATE_PLAYLIST_DIAGNOSIS.json)를따른다.

## Accurate 위치 제한과 다음 검증

현재 HLS Accurate는 검증된 source-start decode를 위해 획득 시작을playlist0으로 지정하고 요청 OUT까지의 참조와 여유를 획득한다. 따라서46분 위치는 현재60초 획득 span과64 segment 계획 범위로 지원할 수 없다. 이번 실제 실패는그보다 앞선playlist byte 검사이며, 아직 나타나지 않은 예산 오류를 이 실행의 실제 오류로 기록하지 않는다. playlist 상한만 늘려도46분 Accurate가 성공한다고 추정하지 않는다.

CHZZK Accurate player 검증에는 검증된 HLS source의앞쪽 구간(예:10~20초)을 사용해야 한다. 새15578184의 추출 문제 또는15683602의 긴playlist 지원은 별도 선행 실험이 필요하며, 임의preroll·전체다운로드·Fast 자동전환으로대체하지 않는다. 앞쪽 위치만으로긴playlist의byte 제한이해결되는것도아니다. 기존6단계 보존 `chzzk-accurate.mp4`를 직접재생해확인하는방법은 새획득없이지원된결과를검증할수있다.

솔루션 build 경고/오류0·Downloader859개 실패/skip0을 확인했다. 새3개 회귀는 실제 관찰한2213866-byte Content-Length, HTTP403, 전송예외를 검사하며 안전한field/message·1회요청·Data없음을 확인한다. Core143/Media193은 이번 변경에서 재실행하지 않았으며 이전 통과 기록과 구분한다. Phase1전체완료와 CHZZK Accurate 사용자확인은Pending이다.

## 후속 사용자 입력과 검증 자료 변경

2026-10-15 사용자는 보존한 CHZZK Accurate 영상이 방송 시작의 무음 부분이어서 재생·음성 동기 검증에 어렵다고 보고했다. 자동 native/decode/PCM 검증 기록은 보존하지만 이 파일만으로 사람이 음성 동기를 확인했다고 판정하지 않는다. 보존 파일을 이용한 사용자 검증 완료 제안은 철회하고, 소리·움직임이 있는 다른 source/앞쪽 구간이 필요하다.

이어14791589·hls-8384·Accurate46:10~46:30·20초 요청에서 `HlsSegmentCountBudget`·필요1396개/상한64개를 보고했다. 이는 사용자 실행의 실제 오류다. 제품의 source-start 획득 시작0 및 planner가 segment count를 fetched span보다 먼저 검사하는 순서와 일치한다. 원인 확인을 위해 영상 전체나1396개 segment를 다운로드하지 않았다. 출력 길이를 줄여도 시작부터46분 OUT까지의 참조 요구는 남는다. count 상한만 높여도현재60초span·byte조건의성공을보장하지않는다.

사용자는 “초반에 소리가 있는 다른 영상으로 현재 지원 범위 검증”을 선택했다. 46분 위치 Accurate 지원 확대는 진행하지 않는다. 새 영상 URL과 소리·움직임이 있는 앞쪽 구간을 요청했으며 아직 제공되지 않았다. 새 입력은 metadata/선택 → 제한된 source-start 획득/native 관계 → Accurate 준비/출력 검증 → 전체decode/정리 → 사용자 재생·음성 동기 순서로 확인한다. 준비·자동 생성 성공과 사람 검증을구별하며 CHZZK Accurate 완료는Pending이다.
