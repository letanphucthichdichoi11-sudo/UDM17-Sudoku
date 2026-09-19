$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
public static class PuzzleAudit {
  public static int CountSolutions(int[] puzzle, int limit) {
    if(puzzle == null || puzzle.Length != 81) return 0;
    var board=(int[])puzzle.Clone();
    for(int i=0;i<81;i++) {
      if(board[i]<0 || board[i]>9) return 0;
      if(board[i]!=0) { int n=board[i]; board[i]=0; if(!Allowed(board,i,n)) return 0; board[i]=n; }
    }
    return Count(board,limit);
  }
  private static bool Allowed(int[] b,int i,int n) {
    int r=i/9,c=i%9;
    for(int j=0;j<9;j++){
      if(b[r*9+j]==n || b[j*9+c]==n) return false;
      int k=(r/3*3+j/3)*9+c/3*3+j%3;
      if(b[k]==n) return false;
    }
    return true;
  }
  private static int Count(int[] b,int limit) {
    int best=-1,bestCount=10;
    for(int i=0;i<81;i++){
      if(b[i]!=0) continue;
      int count=0;for(int n=1;n<=9;n++)if(Allowed(b,i,n))count++;
      if(count==0)return 0;
      if(count<bestCount){best=i;bestCount=count;}
    }
    if(best<0)return 1;
    int total=0;
    for(int n=1;n<=9;n++)if(Allowed(b,best,n)){
      b[best]=n;total+=Count(b,limit-total);b[best]=0;
      if(total>=limit)return total;
    }
    return total;
  }
}
'@

"RUN TC-019 at=$(Get-Date -Format o)"
$sources=@('testing/logs/tcp_room_strict_20260916.log','testing/logs/tcp_duration_strict_20260916.log')
$seen=@{}
foreach($source in $sources){
    foreach($line in (Get-Content -LiteralPath $source)){
        if($line -notmatch '^\{'){continue}
        $record=$line|ConvertFrom-Json
        if($record.Type -ne 14 -or -not $record.Payload.Puzzle){continue}
        $matchId=[string]$record.Payload.MatchId
        if($seen.ContainsKey($matchId)){continue};$seen[$matchId]=$true
        $puzzle=[int[]]@($record.Payload.Puzzle)
        $solutions=[PuzzleAudit]::CountSolutions($puzzle,2)
        $blanks=@($puzzle|Where-Object{$_ -eq 0}).Count
        $given=81-$blanks
        [pscustomobject]@{TestCase='TC-019';Source=$source;MatchId=$matchId;Length=$puzzle.Length;Given=$given;Blanks=$blanks;SolutionsUpTo2=$solutions;ValidUnique=($puzzle.Length -eq 81 -and $solutions -eq 1)}|ConvertTo-Json -Compress
    }
}
"END TC-019 count=$($seen.Count) at=$(Get-Date -Format o)"
