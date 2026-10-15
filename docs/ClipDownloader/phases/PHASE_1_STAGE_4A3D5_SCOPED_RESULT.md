# Phase 1 4a-3d-5 public indexed inspection with unknown shared axis

2026-10-14. 시작 HEAD36cc246, phase/1, Base 유지.

public source service에 HTTPS 분리 선택의 초기 indexed pipeline을 연결했다. 각각 원본 stream kind/config/packet/frame/movie 관계를 검사하고 prefix+참조 body의 누적 budget을 공유한다. 파일은 probe 실행 전에 registry에 등록한다. 실패/취소 및 종료 미확인에서도 소유 파일을 조회할 수 있다.

검사된 video FPS는 scoped fact로 발행한다. 두 개별 movie 시작값이 같다는 이유로 관계를 발행하지 않는다. provider grouping/content 대응이 아직 제품 경로에 없으므로 SharedAxis/Origin/NativeEnd를 Unknown으로 유지한다. Succeeded는 inspection snapshot의 완료이며 실행 준비나 clip 성공이 아니다. target Acquire는 SourceTimelineUnverified로 차단된다. fake Verified나 Best 재선택은 없다.

Downloader595개 통과, solution build 경고0/오류0. 실제 sFXPM_sfJNo의 명시137/140 pair에서 public service 검사 Succeeded, 소유 파일2, FPS30/1, Origin=null/Axis Unknown/End Unknown을 확인했다. Acquire는 Failed/Data=null/SourceTimelineUnverified, 소유2/NeedsCleanup을 유지했다. 초기 두 파일만 수집됐고 target 전송은 없었다. ignored `.artifacts/source-service-real/` safe summaries로 결과를 보존했다.

첫 harness는 Failed의 Data=null을 mapping formatter에서 역참조했다. null filtering 후 재실행하여 위 실패 결과를 기록했다. 이 스크립트 오류를 제품 성공/실패로 바꾸지 않았다. 새 제품 테스트 추가는 없으며 existing HLS 회귀 및 실제 public 경로를 검사했다. 다음 provider/shared-axis evidence 생산 경로와 target/public 통합이 남았다. 전체4a/4단계 완료는 아니다.
