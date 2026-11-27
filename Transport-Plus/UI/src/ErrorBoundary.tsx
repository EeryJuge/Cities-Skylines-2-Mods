import { Component, ReactNode } from "react";

/**
 * Transport+ arayüzündeki bir hata oyunun geri kalan arayüzünü (ve diğer modların pencerelerini) düşürmesin diye
 * her bileşen bu sınırın içinde çizilir. Hata olursa nedeni UI.log'a yazılır; fallback verilmişse hata mesajı
 * gösterilir (pencere kendiliğinden kapanıp "açılmıyor" gibi görünmesin), verilmemişse parça gizlenir.
 */
export class ErrorBoundary extends Component<{ children: ReactNode; onError?: () => void; fallback?: (message: string, retry: () => void) => ReactNode }, { failed: boolean; message: string }> {
  constructor(props: any) {
    super(props);
    this.state = { failed: false, message: "" };
  }
  static getDerivedStateFromError(e: any) {
    return { failed: true, message: String((e && e.message) || e) };
  }
  componentDidCatch(e: any) {
    console.log("[Transport+] UI error: " + ((e && e.stack) || e));
    try { if (this.props.onError) this.props.onError(); } catch (x) { /* yok say */ }
  }
  render() {
    if (!this.state.failed) return this.props.children;
    return this.props.fallback ? this.props.fallback(this.state.message, () => this.setState({ failed: false, message: "" })) : null;
  }
}
