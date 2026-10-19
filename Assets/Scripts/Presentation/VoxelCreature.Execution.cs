using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class VoxelCreature
    {
        Transform skillRing;
        bool skillHighlighted,commanderEliminated,executionPlaying;
        float executionStartedAt;
        public bool SkillHighlighted { get { return skillHighlighted&&!Dead; } }
        public bool IsExecuting { get { return executionPlaying; } }
        internal bool CommanderFigureVisible { get { return figure!=null&&figure.gameObject.activeInHierarchy; } }

        void BuildSkillFeedback()
        {
            if(Kind=="steve")return;
            skillRing=VoxelAssets.Part(transform,"Skill beneficiary preview",VoxelAssets.Cached("skill-beneficiary-ring",b=>{
                Color c=VoxelAssets.Hex("#82F3D6"),gold=VoxelAssets.Hex("#FFE5A0");
                b.Ring(.85f,.81f,c);
                for(int i=0;i<4;i++){
                    Quaternion q=Quaternion.Euler(0,i*90+45,0);
                    b.Box(q*new Vector3(0,0,.94f),new Vector3(.1f,.055f,.16f),gold,q);
                    b.Box(q*new Vector3(0,0,1.05f),new Vector3(.18f,.055f,.055f),gold,q);
                }
            }),new Vector3(0,.06f,0),VoxelAssets.Glow,false);
            skillRing.gameObject.SetActive(false);
        }
        public void SetSkillHighlighted(bool value)
        {
            skillHighlighted=value&&Kind!="steve"&&!Dead;
            if(skillRing!=null)skillRing.gameObject.SetActive(skillHighlighted);
        }
        void StepSkillFeedback(float time,float footprint)
        {
            if(skillRing==null)return;
            skillRing.gameObject.SetActive(skillHighlighted&&!Dead);
            if(!skillHighlighted||Dead)return;
            float size=footprint*(1+Mathf.Sin(time*5+seed)*.035f);
            skillRing.localScale=new Vector3(size,1,size);skillRing.localRotation=Quaternion.Euler(0,Mathf.Sin(time*2)*4,0);
        }

        public void SetCommanderEliminated(bool eliminated,bool waitForExecution)
        {
            if(Kind!="steve")return;
            if(!eliminated){
                if(commanderEliminated){executionPlaying=false;SetCommanderFlash(false);}
                commanderEliminated=false;figure.gameObject.SetActive(true);return;
            }
            commanderEliminated=true;
            if(waitForExecution)figure.gameObject.SetActive(true);
            else if(!executionPlaying)figure.gameObject.SetActive(false);
        }
        public void PlayExecution(float plannedHitTime)
        {
            if(Kind!="steve")return;
            impactAge=10;executionPlaying=true;commanderEliminated=true;executionStartedAt=plannedHitTime;
            figure.gameObject.SetActive(true);
            StepExecution(Time.unscaledTime);
        }
        void StepExecution(float time)
        {
            if(!executionPlaying)return;
            float age=Mathf.Max(0,time-executionStartedAt);
            if(age<ArenaWorld.ExecutionShatterDelay){
                float press=Mathf.Sin(Mathf.Clamp01(age/ArenaWorld.ExecutionShatterDelay)*Mathf.PI);
                figure.localScale=Vector3.Scale(figure.localScale,new Vector3(1+press*.13f,1-press*.28f,1+press*.13f));
                figure.localPosition+=Vector3.up*(press*.34f);
                figure.rotation*=Quaternion.Euler(-press*16,age*120,0);
                SetCommanderFlash(true);
            }
            else {figure.gameObject.SetActive(false);SetCommanderFlash(false);}
            if(age>=ArenaWorld.ExecutionSoulEnd)executionPlaying=false;
        }
        void ResetExtraFeedback()
        {
            commanderEliminated=executionPlaying=false;executionStartedAt=0;SetSkillHighlighted(false);
            if(figure!=null)figure.gameObject.SetActive(true);
        }
    }
}
