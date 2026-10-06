# Git Repository 분리 전략

## 기본 방침

초기에는 **Monorepo(단일 Git 저장소)** 로 관리한다.

프로그램은 서로 독립된 실행 파일로 유지하되, 공통 모듈은 같은 저장소 안에서 공유한다.

```text
ClipMaker/
├── apps/
│   ├── ClipDownloader/
│   └── ClipEditor/
├── src/
│   └── ClipMaker.Media/
├── tests/
├── docs/
└── ClipMaker.sln
```

## 의존성 원칙

```text
       ClipMaker.Media
        ▲         ▲
        │         │
Downloader       Editor
```

- `ClipDownloader`와 `ClipEditor`는 서로 직접 참조하지 않는다.
- 양쪽에서 실제로 사용하는 기능만 공통 모듈로 분리한다.
- FFmpeg 실행, 미디어 정보 분석, Timecode 등은 공통 모듈 후보이다.
- Chzzk/YouTube 다운로드 로직은 Downloader에만 둔다.
- Timeline, Effect, Keyframe 등 편집 기능은 Editor에만 둔다.
- `Common`, `Utils`처럼 목적이 불분명한 잡동사니 프로젝트는 가급적 만들지 않는다.

## 초기에는 저장소를 나누지 않는 이유

Monorepo에서는 공통 모듈을 변경했을 때 Downloader와 Editor를 한 번에 수정하고 테스트할 수 있다.

또한 공통 API 변경과 두 애플리케이션의 대응 작업을 하나의 commit으로 남길 수 있어 개발 및 AI Agent 활용이 편하다.

초기부터 다음과 같이 3개 저장소로 분리하는 것은 권장하지 않는다.

```text
ClipDownloader
ClipEditor
ClipMaker.Media
```

이 경우 공통 모듈의 버전 관리, 패키징, 배포 및 각 저장소의 의존성 업데이트가 필요해져 초기 개발 복잡도가 불필요하게 증가한다.

## 저장소 분리를 고려할 시점

다음과 같은 상황이 생기면 공통 모듈을 별도 저장소로 분리하는 것을 검토한다.

- `ClipMaker.Media`를 다른 프로젝트에서도 사용하게 됨
- 독립적인 버전 및 Release 관리가 필요함
- NuGet 패키지 형태로 배포할 가치가 생김
- Downloader와 Editor의 개발/배포 주기가 크게 달라짐
- 서로 다른 팀이 각 프로젝트를 독립적으로 관리하게 됨

필요하면 이후 다음과 같이 분리할 수 있다.

```text
Repo 1: ClipDownloader
Repo 2: ClipEditor
Repo 3: ClipMaker.Media
```

## 최종 결정

현재 단계에서는 다음 전략을 사용한다.

> **1 Repository + 2 Applications + 필요한 만큼의 Shared Library**

즉, Downloader와 Editor는 별개의 프로그램으로 유지하되 Git 저장소는 하나로 관리한다.

공통 모듈이 실제로 독립적인 라이브러리로 성장한 이후에만 별도 저장소 분리를 검토한다.
