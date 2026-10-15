# Phase 1 4a-3b private source locator·metadata 재검증

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `92c7558665055028a1d59bb3376575ade9a25872`, branch `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-3b metadata/locator 경계 구현·관련 검증 완료**. [4a-3a](PHASE_1_STAGE_4A3A_HTTP_RESULT.md)의 byte reader에 전달할 source 주소/headers를 얻는 기반을 연결했다. 실제 사이트 조회/영상 다운로드·HLS/index/native 해석·`ISelectedSourceInspectionService`/`IClipDownloadService` adapter·4a-3 전체/Phase 완료는 아니다.

## 변경

기존 public YtDlpVideoInfoService의 실행 로직을 **internal YtDlpMetadataQuery**로 분리했다. public 생성자/IVideoInfoService·VideoInfo 결과는 그대로이며 query의 안전한 Info만 반환한다. 실행 옵션·단위 retry2회·metadata120초·node/기능15초·UTF-8/JSON32MiB·stdout 완전성과 root/descendant/drain 확인·취소/timeout/종료 미확인 우선 정책을 재사용한다. 동일 조회 명령을 locator 쪽에서 복제하거나 public 모델에 raw JSON을 추가하지 않았다.

query의 raw JSON은 transient internal response에만 보관하고 같은 응답의 parser 결과와 함께 locator를 구성한다. 내부 locator result는 안전한 VideoInfo도 보관해 후속 adapter가 metadata를 불필요하게 재조회하거나 다른 응답의 format 목록을 섞지 않도록 한다. signed URL/headers는 일반 internal 클래스의 명시적인 getter로만 접근하며 자동 record ToString에 출력하지 않는다. 실제 raw data 파일/로그 저장은 없다. 이 구현이 모든 외부 logger/reflection을 자동 차단하는 것은 아니며 Infrastructure 사용자는 해당 data를 저장/표시하지 않는 계약을 따른다.

`YtDlpSourceLocatorService.ResolveAsync`는 canonical 요청 source로 조회한 뒤 다음을 검사한다.

- 원본 ID·optional webpage URL의 canonical source·완료 영상 상태·metadata duration이 준비 selection과 맞아야 한다.
- 준비 때 선택한 video/audio IDs만 찾는다. 사라진 선택 포맷은 UnsupportedFormat이며 Best 재정렬·다른 format/root URL fallback은 없다.
- 선택한 VideoFormat record의 값은 보수적으로 동일해야 한다. 알려진 FPS의 값 변경/누락·높이/codec/transport 등 변경은 InvalidMetadata다. **최초 FPS Unknown만** fresh metadata의 확인 값으로 해소할 수 있으며 기존 snapshot을 수정하지 않는다. 이는 native FPS/CFR 검증 완료를 뜻하지 않는다. 다른 metadata field의 unknown 해소나 estimate 변경을 이 단위에서 임의로 동일 asset이라고 승격하지 않는다.
- 선택 entry의 url은 고유한 string HTTPS여야 하며 credential user-info·fragment·공백/control 문자를 거절한다. optional manifest_url도 같은 규칙이다. 주소 token 갱신은 위 선택 재검증 후 허용하지만 asset generation/source-native 동일성은 이후 실제 bytes/관찰에서 검증한다.
- root/format별 http_headers를 검증하고 format header가 같은 이름의 root 값을 override한다. duplicate case-insensitive 이름·잘못된 type·CRLF는 InvalidMetadata, 미지원/인증 header는 UnsupportedFormat이다. 조용히 버리거나 자동 인증/cookie를 추가하지 않는다. 지원 header에 **Sec-Fetch-Mode**를 명시 추가해 새 metadata fixture→HTTP reader 전달을 검증했다. 실제 사이트가 그 header를 요구한다는 새 실험 결과를 주장하지 않는다.

locators는 JsonDocument 수명 밖에서도 유효하도록 URI/string·header dictionary·기존 immutable VideoFormat으로 materialize한다. 대응이 모순/미확인이면 bytes를 가져오거나 native 관계/coverage 성공으로 설명하지 않는다. private locator Resolve 성공은 주소/metadata 경계 결과일 뿐 clip 준비/획득 성공이 아니다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore` | 최종 exit0, 통과354/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

새27개 사례와 기존 public metadata 조회·실행 진단·parser·HTTP/contract 회귀를 포함했다. selected pair/header merge·SourceHttpRequest 연결, public snapshot/ToString의 locator data 비노출, 최초 Unknown FPS 해소, 알려진 값/identity/완료 상태/duration 변경, unsafe/malformed URI·duplicate url·missing audio/root fallback 차단, invalid/unsupported headers, locator token 갱신·취소/불완전 출력·timeout/종료 미확인을 검사했다.

최초 테스트 실행은 fixture raw 문자열 문법 오류로 컴파일 실패했고 수정 후 ReadOnlyMemory 초기화 오류가 남아 두 번째 컴파일도 실패했다. fixture를 수정한 뒤353개가 통과했고, 같은 응답 정보 보관/locator 갱신·root fallback 검증을 보완해 최종354개를 통과했다. 제품 빌드는 최종 경고/오류0이다. 실패를 source 실패나 성공 실험으로 숨기지 않는다.

IProcessRunner fake와 합성 JSON/예약 example URI만 사용했으며 실제 yt-dlp/node/HTTP/CDN/미디어 실행은 없다. package/project/솔루션 등록·DI host·새 Application/Core public data는 변경하지 않았다. Core/Media.Infrastructure 전체 테스트를 이번에 합산하지 않고 WPF는 솔루션 빌드만 확인했다.

## 다음 단위와 커밋 경계

다음 **4a-3c**는 먼저 CHZZK 선택 HLS의 manifest/playlist 선택·bounded 초기/target 참조 계획/읽기를 연결한다. master/variant·selected format·HTTP/URI 조건과 원본 reference를 확인하고 native 판단·파일 ownership/snapshot·실제 inspection/acquisition 구현으로 이어간다. codec/frame/native 증거가 없는 구간을 성공으로 추정하지 않는다. 이후 YouTube indexed-source 경로를 같은 선택/byte/snapshot gate로 연결한다.

도구 재조회 결과만으로 source origin/mapping/coverage·decoder 준비를 증명하지 않는다. 실제 bounded 사이트 검증·프로세스/HTTP 취소·native/end 의미와 final processing 검증은 여전히 남아 있다. Phase2·WPF 다운로드 UI·ClipEditor를 진행하지 않는다.

이번 커밋은 Infrastructure 신규3파일/기존2파일·test1파일·결과/설계/source 계약/baseline4문서다. 기존 정책의 회귀·변경 범위·공백/링크·private 경계를 확인했다. staging/commit은 하지 않았고 사용자·기존 실험 자료를 보존했다. 권장 메시지: `feat: Resolve private source locators with selection validation`.
