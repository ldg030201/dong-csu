class DongMcu < Formula
  desc "Claude 사용량을 화면 위에 항상 띄워두는 macOS HUD"
  homepage "https://github.com/ldg030201/dong-mcu"
  url "https://github.com/ldg030201/dong-mcu/archive/refs/tags/v1.3.1.tar.gz"
  sha256 "e2f27f6c3f80265da33cf9d312ce4e31074409c5dd59b75214181c93571df552"
  license "MIT"
  head "https://github.com/ldg030201/dong-mcu.git", branch: "main"

  depends_on macos: :sonoma

  def install
    # Homebrew의 빌드 샌드박스 안에서는 SwiftPM이 매니페스트를 평가할 때 쓰는
    # 자체 샌드박스가 중첩되어 실패한다.
    ENV["SWIFT_BUILD_FLAGS"] = "--disable-sandbox"
    system "./build.sh"
    prefix.install "build/DongMCU.app"
    # 앱 이름은 DongMCU지만 명령 이름은 dong-mcu 그대로 둔다.
    bin.install_symlink prefix/"DongMCU.app/Contents/MacOS/DongMCU" => "dong-mcu"
  end

  def caveats
    <<~EOS
DongMCU는 Claude Code가 keychain에 저장한 OAuth 토큰을 읽습니다.
      Claude Code에 로그인되어 있어야 동작하고, 첫 실행 때 keychain 접근 허용을 한 번 묻습니다.

      /Applications 에 등록하고 실행하려면 (formula는 GUI 앱이라도 /Applications 를 건드리지 않습니다):
        cp -R #{opt_prefix}/DongMCU.app /Applications/ && open /Applications/DongMCU.app

      심볼릭 링크로 걸면 Launchpad·Spotlight가 앱으로 인식하지 못합니다. 복사해야 합니다.
      업그레이드한 뒤에도 /Applications 쪽은 다시 복사해야 새 버전이 됩니다.

      로그인하면 자동으로 시작하게 하려면 시스템 설정 > 일반 > 로그인 항목에 추가하세요.
    EOS
  end

  test do
    assert_match "dong-mcu", shell_output("#{bin}/dong-mcu --version")
  end
end
