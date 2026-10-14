# Phase 1 4단계 통합 검증 기록

2026-10-14, `phase/1`, Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지. 이 문서는 누적 작업 기록이며 **4단계 완료 보고가 아니다**. 개별 커밋 보고를 생략하고 4단계 완료 후 통합 보고하는 사용자 지시를 따른다.

## 획득 및 공유 probe

indexed/보조 HLS collector에 기존 HLS collector와 같은 trusted storage test seam을 연결했다. write/flush/close/delete 실패, 전송 중·쓰기 중·close 이후 취소를 검사했다. 불확실한 close에서는 삭제를 시도하지 않고 소유 파일을 인계하며, 실패·취소 결과의 Data는 null이다. 사용자 파일은 유지한다. 보조 collector의 CreateNew 충돌은 OutputConflict로 구별한다. 추가 lifecycle 16개를 포함한 Downloader Infrastructure 714개 통과.

`IMediaProbe`와 local request/result, immutable raw stream/frame/packet/skip 관찰을 공유 Media.Core에 구현했다. 요청에는 local snapshot/file identity, 절대경로, 기대 길이/SHA-256, Summary 또는 FramesAndPackets, 상세 요청의 유한 range가 필요하다. 상세 관찰은 소유 파일 전체를 검사하여 reorder/준비 관찰을 보존하며 요청 range로 seek하지 않는다. requested range는 native coverage 증명이 아니다.

`FfprobeMediaProbe`의 초기 전략은 local MP4, protocol=file/format=mov다. 읽기 공유 lock과 실행 전후 길이/SHA-256, 버전15초·probe60초, stdout Full 32MiB 상한과 EOF/완전성, stderr 오류, root/descendant/stream 종료를 확인한다. 종료 불확실은 취소보다 먼저 실패한다. raw 관찰은 Demux이며 missing PTS를 DTS/best effort/0으로 대체하지 않는다. declared/average rate는 cadence 증명이 아니다. native header 해석·qualification·decoder preparation 성공과 구분한다.

기존 초기 획득 parser는 공유 strict parser에 위임하되 기존 8MiB/Downloader 오류 경계를 유지한다. 공유 parser는 JSON32MiB/stream16/frame100,000/packet100,000/depth32의 상한을 갖는다. signed integer clock, audio sample 수, skip/discard와 미해석 side data를 보존한다. summary는 frame/packet 관찰을 암묵적으로 만들지 않는다.

기존 소유 로컬 파일로 실제 FFprobe8.1 공유 경로를 실행했다. 이 실험에서는 현재 local bytes를 identity로 고정했으며 원격 asset 전체 동일성이나 native 해석 성공을 주장하지 않는다.

| 입력 | bytes | frames / packets | 결과 |
|---|---:|---:|---|
| YouTube137 first indexed | 1,338,181 | 180 / 180 | raw Demux 성공 |
| YouTube140 first indexed | 162,728 | 430 / 430 | raw Demux 성공 |
| CHZZK 기존 first HLS | 2,050,886 | 213 / 213 | raw Demux 성공 |
| 기존 AAC edit 반례 | 193,792 | 535 / 536 | raw Demux 성공, PTS−1024/skip1024 보존; native/Accurate 지원 성공 아님 |

실행 harness는 ignored `.artifacts/initial-probe-real/ProbeShared.ps1`이다. 새 다운로드·설치·project 생성은 없었다. Core143/Media Infrastructure90/Downloader Infrastructure714개 모두 실패·skip0. 솔루션 build 경고·오류0. Media Infrastructure90개에는 실제 ProcessRunner fixture 회귀 검증이 포함된다.

## 남은 4단계 gate

- 4a의 제한된 획득 전략 최종 audit와 지원/거절 조건 확인.
- 4b의 shared native header/sample 해석 및 획득 snapshot 연결. raw probe 성공으로 native coverage/codec preparation을 승격하지 않는다.
- 4c qualification, K/common shift/Fast 출력, Accurate source-start decoder 준비/PCM 재구성, staging·actual output/native/edit/end 검증.
- 양 사이트/양 모드 실제 clip 및 실패·취소 통합. 전체 Phase 1과 후속5/6은 미완료다.

Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다. 설치·범용 format 확장·선택 포맷 변경·whole-video fallback·허용 오차 추가를 하지 않는다.
