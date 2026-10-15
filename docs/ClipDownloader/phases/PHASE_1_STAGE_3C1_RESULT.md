# Phase 1 / 3c 첫 논리적 단위 — 화질 요청·포맷 선택 계약

수행일: 2026-10-10 (Asia/Seoul).

상태: **3c-1 계약 구현·검증 완료, 커밋 대기**. 3c 전체 완료는 아니다. Best/음성 순위·모드별 codec 호환성의 실험과 자동 FormatSelector 구현이 남아 있다.

## 시작·범위

- branch `phase/1`, 시작 HEAD `c39befd61336bf061c497180c772ea4bf84eaa21`: `feat: Add video metadata retrieval and JSON validation`. 시작 작업 트리는 깨끗했다. 3b 커밋을 확인했다.
- 상세 설계 제4.22절의 채택한 QualityOption/FormatSelection을 Application에 구현했다. 기존 VideoInfo·VideoFormat·OperationResult·FrameRate를 사용한다.
- 순위 계산과 독립적인 계약을 한 커밋 단위로 분리했다. 추정 정렬이나 고정 codec 우선순위로 미완료 실험을 대신하지 않았다.
- 기존 Downloader.Infrastructure.Tests에 Application 계약 테스트 26개를 추가했다. 이 테스트들은 Infrastructure 서비스·프로세스·네트워크를 호출하지 않는다. 현재 프로젝트가 Application을 참조하므로 이 단위에는 별도 test project/패키지가 필요하지 않았다.
- 프로젝트/솔루션·패키지·도구 설치·영상 다운로드·제품 CLI/WPF·Phase 2·ClipEditor·Git staging/commit/tag/branch 변경은 수행하지 않았다.

## 구현 계약

- QualityOption은 불변 Best 또는 양의 높이 요청이다. Best에 임의 기본 높이를 넣지 않으며 값 equality를 제공한다. 지정 높이가 양수라는 검사는 실제 제공 여부 확인이 아니다. CLI 문자열 parsing은 이후 호스트 단위에 남긴다.
- FormatSelection은 요청 화질, 선택 video, nullable 별도 audio, 타입화된 사유, 복사한 read-only 주의사항을 보존한다. FPS는 선택 video에만 있고 중복 필드가 없다.
- `FormatSelection.Create`는 **호출자가 명시한 후보 조합**을 검사한다. 목록을 정렬하거나 자동으로 후보를 고르지 않는다. RequestedBest 사유는 요청 종류를 나타내며 Best 품질 순위가 검증됐다는 뜻이 아니다.
- 선택 video/audio는 조회 snapshot에 값이 일치하고 각각 ID가 유일해야 한다. 다른 snapshot의 같은 ID·변경된 값·중복 ID·음성 전용 video·잘못된 stream enum은 거부한다.
- 지정 해상도는 선택 video의 높이와 정확히 같아야 한다. 높이 미확인·다른 높이는 UnsupportedFormat/Input/Quality이며 조용히 확대·축소하지 않는다.
- 별도 audio는 영상 스트림 Absent·음성 Present인 확인된 음성 전용 포맷이어야 한다. video의 음성이 Absent일 때만 별도 audio를 허용한다. 통합 A/V나 embedded audio 미확인 포맷에 음성을 임의 중복/대체하지 않는다.
- 미확인 video/audio stream·해상도·FPS·codec은 주의사항으로 보존한다. 영상 전용 포맷에 별도 audio가 없으면 원본이 무음이라고 단정하지 않고 AudioSourceRequiresVerification을 기록한다. 추가 확인 전 이 모델을 실행 가능한 ClipJob으로 간주하지 않는다. 원본 무음이 확인된 경우의 실행 허용은 기존 일반 정책을 유지한다.
- 후보 모델 생성 성공은 원본의 완료 상태·길이·CFR·mode compatibility·최종 음성 보존·획득 가능성의 검증 성공이 아니다. 해당 검사는 이후 준비/실험/실행 단위에서 수행한다.

## 실제 검증

- `dotnet build ClipMaker.slnx --no-restore`: 경고 0·오류 0.
- `dotnet test ClipMaker.slnx --no-build --no-restore`: Core 85·Media.Infrastructure 73·Downloader.Infrastructure 243 모두 통과, **합계 401개**.
- 사유 enum을 순위 계산이 완료된 것처럼 읽히지 않도록 RequestedBest로 명명한 후 최종 전체 빌드와 영향받은 Downloader 테스트 243개를 다시 통과했다. 나머지 프로젝트의 동작 변경은 없으며 통과한 테스트를 불필요하게 반복하지 않았다.
- 신규 26개는 양수 해상도/equality, 정확한 높이·미확인 높이, 통합/분리 A/V, 영상 FPS 보존, 원본 무음 오판 방지, unknown 값, snapshot/중복 ID, 잘못된 조합/enum·사유, read-only 주의사항을 검증한다.
- 실제 사이트 조회·획득·encode/probe 실험은 이번 단위에서 실행하지 않았다. `git diff --check`로 변경 형식도 확인했다.

## 미완료와 다음 단위

이 단위의 권장 커밋 메시지: `feat: Add quality and format selection contracts`.

설계 제4.22절·제7절 및 계약 계획의 3c 행은 Best/codec 순위를 실험 후 확정하도록 한다. 현재 영상 다운로드 금지와 함께 적용하면, 3b metadata만으로 자동 순위를 구현할 수 없다. 이것은 이미 채택한 일반 정책 재승인 요청이 아니다.

다음에 실행할 구체적 범위·format ID·구간·인자·판정 기준은 [3c 잔여 실험 계획](PHASE_1_STAGE_3C_EXPERIMENT_PLAN.md)에 준비했다. 사용자 URL 두 개에 대해 각각 [90,100)초, CHZZK 두 format·YouTube 세 video/audio 조합, 최대 다섯 구간 요청이다. 다운로드 제한 해제 전에는 실행하지 않는다. 실험에서 실제 조회값이 달라졌거나 format이 사라지면 자동 대체하지 않는다.

근거 확보 후 Best/음성 순위·동률·호환성 처리를 구체화하고 FormatSelector를 다음 논리적 커밋으로 구현한다. 전체 Phase 1 완료와 정확한 시간축·CFR/Accurate·출력 검증은 계속 Pending이다.
