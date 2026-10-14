# Phase 1 후속 — CHZZK 30초 요청의 획득 상한 진단

2026-10-14. 시작 HEAD `36acc78`, branch `phase/1`, clean에서 사용자 CHZZK 실패 보고를 확인했다. 기존 일반 정책과 32 MiB / 30초 획득 상한을 유지한다. 이번 변경은 실패 원인 표시 보완이며 해당 30초 클립 생성 지원 완료가 아니다.

후속: [제한 예산 보완·실제 지원](PHASE_1_CHZZK_FETCH_BUDGET_SUPPORT.md)에서 비용 실험 후 CLI 기본값을 64 MiB / 40초로 조정했다. 아래 미지원/예산 유지 설명은 진단 커밋 `d639195` 시점의 기록이다.

## 확인된 원인

입력은 CHZZK `14791589`, best `hls-8384`, 60/1 FPS, Fast, IN `00:46:10:00` / OUT `00:46:40:00`다. 요청 길이는 30초다.

실제 metadata·선택 playlist·제한된 첫 native 참조를 읽었다. 첫 참조 span은 2초, 검증된 원점은 `19187/9600`, video observed start는 2초다. 기존 Fast 전략은 요청 앞뒤에 첫 참조 span을 더하므로 playlist fetch window는 `[26572787/9600,26899187/9600)`이다. 이 범위와 겹치는 whole segment는 sequence 1383~1400의 18개이며, 실제 획득 범위는 `[2766,2802)`, **36초**다. playlist 범위 밖이나 64개 세그먼트 상한 문제가 아니라 **whole-reference 시간 상한 30초 초과**다.

원래 planner는 playlist 범위·segment coverage·개수·시간 상한 실패를 같은 메시지와 null field로 표시했다. 이제 `HlsPlaylistRange`, `HlsSegmentCoverage`, `HlsSegmentCountBudget`, `HlsFetchedSpanBudget`로 구별하고 필요한 개수/정확한 유리수 초와 상한을 안내한다. 오류 code/category와 획득·native·출력 검사는 유지한다. 메시지에 signed URL·headers·원시 metadata를 넣지 않는다.

## 검증 결과

- 솔루션 `dotnet build ClipMaker.slnx --no-restore --nologo`: 경고/오류 0.
- Downloader Infrastructure 회귀: 848개 통과, 실패/skip 0. 기존 whole-segment exclusive OUT·상한 비교에 더해 분수 span과 정확한 경계 허용, 30초 요청에 Fast 여유를 더한 실패를 확인했다. Core/Media 테스트는 이번에 재실행하지 않았으며 이전 143/159 통과 기록과 구분한다.
- 같은 요청을 새 고유 출력 경로와 `--yes`로 실제 CLI 실행: exit 1, `HlsFetchedSpanBudget`, “HLS 세그먼트 전체 획득 범위 36초가 상한 30초를 초과합니다.”를 확인했다. 목표 segment 다운로드 전에 실패하고 final을 생성하지 않았다.
- 실제 CLI job `723a9205-48ef-467b-92d3-56bbb82fe87b`의 작업 폴더 부재와 출력 폴더의 staging 부재를 확인했다. 사용자 `out.mp4`는 조작하지 않았다. 별도 진단의 첫 참조는 ignored 증거 폴더에 보존한다.

진단 harness의 최초 두 실행은 playlist 조회 상한을 제품의 2 MiB보다 낮은 1 MiB로 둬 실패했다. 이후 제품과 같은 상한으로 맞췄으며 다음 실행의 PowerShell 예약 변수 충돌을 수정해 위 최종 관찰을 얻었다. 이 중간 실패를 제품 오류나 성공 증거로 세지 않는다.

원시 진단은 `.artifacts/chzzk-span-diagnosis/38cd2abb255248beacb560e452138cc6/summary.json`, 실제 CLI stdout/stderr는 `.artifacts/chzzk-span-cli/67cab45351954c9e891b09ed08152993/`에 있다. 영속화한 최소 관찰은 [JSON 증거](evidence/PHASE_1_CHZZK_FETCH_BUDGET_DIAGNOSIS.json)를 따른다.

## 남은 지원 작업

30초 원본 획득 상한은 30초 출력 클립 지원을 뜻하지 않는다. 현재 Fast 여유·whole-reference 선택을 유지하면 영상 중간의 30초 요청을 지원할 수 없다. 더 짧은 구간도 segment 경계에 따라 상한을 넘을 수 있으며 byte/native/output 검사 성공을 보증하지 않는다.

이 입력의 계획 단계만 통과하려면 최소 36초의 시간 budget이 필요하다. **36초로 늘리면 생성이 성공한다는 의미는 아니다.** 목표 segment bytes를 받지 않아 32 MiB 누적 상한·목표 구간 native/media/output 대응은 아직 미검증이다. 다음 지원 보완은 제한된 예산 조정 실험으로 해당 비용과 양 모드의 실제 검사를 확인한 뒤 내부 상한/지원 범위를 결정해야 한다. 시간 여유를 증거 없이 제거하거나 전체 다운로드·화질/모드 fallback으로 우회하지 않는다. 먼 위치의 Accurate는 기존 source-start prefix 전략 때문에 별도 제한이 있다.

직접 player의 영상/음성·동기 확인과 Phase 1 전체 Passed/Completion/Tag는 계속 Pending이다. Phase 2·WPF 다운로드 UI·ClipEditor로 진입하지 않았다.
